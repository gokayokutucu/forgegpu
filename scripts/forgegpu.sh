#!/usr/bin/env bash
# forgegpu.sh — local operational entrypoint for ForgeGPU
# Usage: ./scripts/forgegpu.sh <command> [flags]
set -euo pipefail

# ---------------------------------------------------------------------------
# Resolve repo root regardless of working directory
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
API_BASE="${FORGEGPU_API_BASE:-http://localhost:8080}"
DEFAULT_MODEL="gpt-sim-a"
DEFAULT_WEIGHT=100
DEFAULT_SMOKE_JOBS=3
DEFAULT_TIMEOUT=60

# ---------------------------------------------------------------------------
# Color helpers (no-op when not a terminal or NO_COLOR is set)
# ---------------------------------------------------------------------------
if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
  RED='\033[0;31m'
  GREEN='\033[0;32m'
  YELLOW='\033[1;33m'
  CYAN='\033[0;36m'
  BOLD='\033[1m'
  RESET='\033[0m'
else
  RED='' GREEN='' YELLOW='' CYAN='' BOLD='' RESET=''
fi

info()    { echo -e "${CYAN}[forge]${RESET} $*"; }
ok()      { echo -e "${GREEN}[ok]${RESET}    $*"; }
warn()    { echo -e "${YELLOW}[warn]${RESET}  $*"; }
die()     { echo -e "${RED}[error]${RESET} $*" >&2; exit 1; }
sep()     { echo -e "${BOLD}────────────────────────────────────────${RESET}"; }

# ---------------------------------------------------------------------------
# Tool detection — checked once at startup
# ---------------------------------------------------------------------------
HAS_JQ=false
HAS_PYTHON3=false
HAS_K6=false
command -v jq      &>/dev/null && HAS_JQ=true
command -v python3 &>/dev/null && HAS_PYTHON3=true
command -v k6      &>/dev/null && HAS_K6=true

# ---------------------------------------------------------------------------
# Safe JSON payload builder
# Supports prompts/models containing quotes, backslashes, newlines, apostrophes.
# Requires jq or python3.
# ---------------------------------------------------------------------------
build_json_payload() {
  local prompt="$1"
  local model="$2"
  local weight="$3"
  if [[ "${HAS_JQ}" == "true" ]]; then
    jq -n \
      --arg     prompt  "${prompt}" \
      --arg     model   "${model}" \
      --argjson weight  "${weight}" \
      '{"prompt":$prompt,"model":$model,"weight":$weight}'
  elif [[ "${HAS_PYTHON3}" == "true" ]]; then
    python3 - "${prompt}" "${model}" "${weight}" <<'PYEOF'
import json, sys
payload = {"prompt": sys.argv[1], "model": sys.argv[2], "weight": int(sys.argv[3])}
print(json.dumps(payload))
PYEOF
  else
    die "jq or python3 is required to build JSON payloads — install one and retry"
  fi
}

# ---------------------------------------------------------------------------
# Safe JSON field extractor
# Usage: json_field <json_string> <field_name>
# Prints the field value (empty string if field absent or null).
# Falls back to a minimal grep/cut approach if neither jq nor python3 is available.
# ---------------------------------------------------------------------------
json_field() {
  local json="$1"
  local field="$2"
  if [[ "${HAS_JQ}" == "true" ]]; then
    printf '%s' "${json}" | jq -r --arg f "${field}" '.[$f] // empty'
  elif [[ "${HAS_PYTHON3}" == "true" ]]; then
    printf '%s' "${json}" | python3 -c "
import json, sys
try:
    d = json.load(sys.stdin)
    v = d.get(sys.argv[1])
    print('' if v is None else v)
except Exception:
    pass
" "${field}"
  else
    # Minimal fallback — reliable only for simple string values without special characters
    printf '%s' "${json}" | grep -o "\"${field}\":\"[^\"]*\"" | head -1 | cut -d'"' -f4
  fi
}

# ---------------------------------------------------------------------------
# HTTP GET — validates 2xx status, returns body on success, dies on error.
# Usage: body=$(http_get <url>)
# ---------------------------------------------------------------------------
http_get() {
  local url="$1"
  local tmp
  tmp=$(mktemp)
  local code
  # shellcheck disable=SC2064
  trap "rm -f '${tmp}'" RETURN
  code=$(curl -s --max-time 10 -o "${tmp}" -w "%{http_code}" "${url}" 2>/dev/null) \
    || die "Connection failed: GET ${url}"
  local body
  body=$(cat "${tmp}")
  if [[ "${code:0:1}" != "2" ]]; then
    local preview="${body:0:200}"
    die "HTTP ${code} from GET ${url}${preview:+  — ${preview}}"
  fi
  printf '%s' "${body}"
}

# ---------------------------------------------------------------------------
# HTTP POST (JSON) — validates 2xx status, returns body on success, dies on error.
# Usage: body=$(http_post_json <url> <payload>)
# ---------------------------------------------------------------------------
http_post_json() {
  local url="$1"
  local payload="$2"
  local tmp
  tmp=$(mktemp)
  local code
  # shellcheck disable=SC2064
  trap "rm -f '${tmp}'" RETURN
  code=$(curl -s --max-time 10 \
    -X POST \
    -H "Content-Type: application/json" \
    -d "${payload}" \
    -o "${tmp}" -w "%{http_code}" \
    "${url}" 2>/dev/null) \
    || die "Connection failed: POST ${url}"
  local body
  body=$(cat "${tmp}")
  if [[ "${code:0:1}" != "2" ]]; then
    local preview="${body:0:200}"
    die "HTTP ${code} from POST ${url}${preview:+  — ${preview}}"
  fi
  printf '%s' "${body}"
}

# ---------------------------------------------------------------------------
# Pretty-print JSON if jq is available, otherwise pass through
# ---------------------------------------------------------------------------
maybe_jq() {
  if [[ "${HAS_JQ}" == "true" ]]; then
    jq .
  else
    cat
  fi
}

resolve_k6_api_base() {
  local api_base="$1"
  if [[ "${HAS_K6}" == "true" ]]; then
    printf '%s' "${api_base}"
    return 0
  fi

  if [[ "${api_base}" == http://localhost:* ]]; then
    printf '%s' "${api_base/localhost/host.docker.internal}"
    return 0
  fi

  if [[ "${api_base}" == http://127.0.0.1:* ]]; then
    printf '%s' "${api_base/127.0.0.1/host.docker.internal}"
    return 0
  fi

  printf '%s' "${api_base}"
}

run_k6_script() {
  local script_path="$1"
  shift

  if [[ "${HAS_K6}" == "true" ]]; then
    k6 run "$@" "${script_path}"
    return 0
  fi

  local docker_api_base
  docker_api_base=$(resolve_k6_api_base "${API_BASE}")

  docker run --rm -i \
    --add-host=host.docker.internal:host-gateway \
    -e API_BASE_URL="${docker_api_base}" \
    -e VUS="${VUS:-}" \
    -e ITERATIONS="${ITERATIONS:-}" \
    -e MAX_DURATION="${MAX_DURATION:-}" \
    -e MODEL="${MODEL:-}" \
    -e REQUIRED_MEMORY_MB="${REQUIRED_MEMORY_MB:-}" \
    -e WEIGHT="${WEIGHT:-}" \
    -e POLL_INTERVAL_MS="${POLL_INTERVAL_MS:-}" \
    -e POLL_TIMEOUT_MS="${POLL_TIMEOUT_MS:-}" \
    -v "${REPO_ROOT}:/work" \
    -w /work/load-tests \
    grafana/k6 run "$@" "$(basename "${script_path}")"
}

# ---------------------------------------------------------------------------
# Environment bootstrap
# ---------------------------------------------------------------------------
ensure_env() {
  local env_file="${REPO_ROOT}/.env"
  local example="${REPO_ROOT}/.env.example"
  if [[ ! -f "${env_file}" ]]; then
    if [[ -f "${example}" ]]; then
      info "No .env found — copying from .env.example"
      cp "${example}" "${env_file}"
      ok ".env created from .env.example (review values before running in production)"
    else
      warn "Neither .env nor .env.example found — docker compose will use built-in defaults"
    fi
  fi
}

# ---------------------------------------------------------------------------
# Docker Compose wrapper (always run from repo root)
# ---------------------------------------------------------------------------
dc() {
  docker compose --project-directory "${REPO_ROOT}" "$@"
}

# ---------------------------------------------------------------------------
# Health check helper — returns 0 if API is healthy, 1 otherwise.
# Does NOT die — used internally for polling during startup.
# ---------------------------------------------------------------------------
check_health() {
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "${API_BASE}/health" 2>/dev/null) \
    || return 1
  [[ "${code}" == "200" ]]
}

# ---------------------------------------------------------------------------
# Wait for API to become healthy, up to $1 seconds
# ---------------------------------------------------------------------------
wait_for_api() {
  local timeout="${1:-${DEFAULT_TIMEOUT}}"
  local elapsed=0
  info "Waiting for API at ${API_BASE}/health (timeout: ${timeout}s)..."
  while ! check_health; do
    if (( elapsed >= timeout )); then
      die "API did not become healthy within ${timeout}s"
    fi
    sleep 2
    elapsed=$(( elapsed + 2 ))
    printf '.'
  done
  echo ""
  ok "API is healthy"
}

# ---------------------------------------------------------------------------
# Submit a single job — prints the job id on success, dies on error.
# Payload is built safely; supports arbitrary prompt/model strings.
# ---------------------------------------------------------------------------
submit_job() {
  local prompt="$1"
  local model="${2:-${DEFAULT_MODEL}}"
  local weight="${3:-${DEFAULT_WEIGHT}}"
  local payload
  payload=$(build_json_payload "${prompt}" "${model}" "${weight}")
  local response
  response=$(http_post_json "${API_BASE}/jobs" "${payload}")
  local jid
  jid=$(json_field "${response}" "id")
  if [[ -z "${jid}" ]]; then
    die "Server accepted the request but returned no job id — response: ${response:0:200}"
  fi
  printf '%s' "${jid}"
}

# ---------------------------------------------------------------------------
# Poll a job until Completed or Failed, up to $timeout seconds.
# Prints the final status string and returns:
#   0 — Completed or Failed (job reached a terminal state)
#   1 — Timeout, connection error, or non-2xx HTTP response
# Distinguishes: Completed | Failed | Timeout | ConnectionError | HTTPError:<code>
# ---------------------------------------------------------------------------
poll_job() {
  local job_id="$1"
  local timeout="${2:-${DEFAULT_TIMEOUT}}"
  local elapsed=0
  local tmp
  while true; do
    tmp=$(mktemp)
    local code
    code=$(curl -s --max-time 5 \
      -o "${tmp}" -w "%{http_code}" \
      "${API_BASE}/jobs/${job_id}" 2>/dev/null) || code="000"
    local response
    response=$(cat "${tmp}")
    rm -f "${tmp}"

    if [[ "${code}" == "000" ]]; then
      # Connection failure — transient; keep waiting until timeout
      :
    elif [[ "${code:0:1}" != "2" ]]; then
      # Non-2xx — definitive HTTP error, no point retrying
      printf 'HTTPError:%s' "${code}"
      return 1
    else
      local status
      status=$(json_field "${response}" "status")
      if [[ "${status}" == "Completed" || "${status}" == "Failed" ]]; then
        printf '%s' "${status}"
        return 0
      fi
    fi

    if (( elapsed >= timeout )); then
      if [[ "${code}" == "000" ]]; then
        printf 'ConnectionError'
      else
        printf 'Timeout'
      fi
      return 1
    fi
    sleep 2
    elapsed=$(( elapsed + 2 ))
  done
}

# ===========================================================================
# Subcommand: up
# ===========================================================================
cmd_up() {
  local do_build=false
  local detach=false
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --build)     do_build=true ;;
      --detach|-d) detach=true ;;
      *) die "Unknown flag for 'up': $1" ;;
    esac
    shift
  done

  ensure_env
  cd "${REPO_ROOT}"

  local compose_args=()
  [[ "${detach}"   == "true" ]] && compose_args+=(-d)
  [[ "${do_build}" == "true" ]] && compose_args+=(--build)

  info "Starting ForgeGPU stack..."
  dc up "${compose_args[@]+"${compose_args[@]}"}"
}

# ===========================================================================
# Subcommand: down
# ===========================================================================
cmd_down() {
  local remove_volumes=false
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --volumes|-v) remove_volumes=true ;;
      *) die "Unknown flag for 'down': $1" ;;
    esac
    shift
  done

  local compose_args=()
  [[ "${remove_volumes}" == "true" ]] && compose_args+=(-v)

  info "Stopping ForgeGPU stack..."
  dc down "${compose_args[@]+"${compose_args[@]}"}"
  ok "Stack stopped"
}

# ===========================================================================
# Subcommand: build
# ===========================================================================
cmd_build() {
  local do_docker=false
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --docker) do_docker=true ;;
      *) die "Unknown flag for 'build': $1" ;;
    esac
    shift
  done

  info "Building ForgeGPU.sln..."
  dotnet build "${REPO_ROOT}/ForgeGPU.sln"
  ok "dotnet build succeeded"

  if [[ "${do_docker}" == "true" ]]; then
    ensure_env
    info "Building docker compose images..."
    dc build
    ok "Docker images built"
  fi
}

# ===========================================================================
# Subcommand: health
# ===========================================================================
cmd_health() {
  info "Checking API health at ${API_BASE}/health..."
  local response
  response=$(http_get "${API_BASE}/health")
  printf '%s\n' "${response}" | maybe_jq
  local status_val
  status_val=$(json_field "${response}" "status")
  if [[ "${status_val}" == "ok" ]]; then
    ok "API is healthy"
  else
    die "API health check returned unexpected status: '${status_val}'"
  fi
}

# ===========================================================================
# Subcommand: workers
# ===========================================================================
cmd_workers() {
  info "Fetching worker states from ${API_BASE}/workers..."
  local response
  response=$(http_get "${API_BASE}/workers")
  printf '%s\n' "${response}" | maybe_jq
}

# ===========================================================================
# Subcommand: smoke
# ===========================================================================
cmd_smoke() {
  local job_count="${DEFAULT_SMOKE_JOBS}"
  local weight="${DEFAULT_WEIGHT}"
  local weights=""
  local model="${DEFAULT_MODEL}"
  local timeout="${DEFAULT_TIMEOUT}"
  local do_build=false
  local check_workers=false

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --jobs)     job_count="${2:?'--jobs requires a value'}"; shift ;;
      --weight)   weight="${2:?'--weight requires a value'}"; shift ;;
      --weights)  weights="${2:?'--weights requires a value'}"; shift ;;
      --model)    model="${2:?'--model requires a value'}"; shift ;;
      --timeout)  timeout="${2:?'--timeout requires a value'}"; shift ;;
      --build)    do_build=true ;;
      --workers)  check_workers=true ;;
      *) die "Unknown flag for 'smoke': $1" ;;
    esac
    shift
  done

  # Optionally build and start the stack (detached) before running the test
  if [[ "${do_build}" == "true" ]]; then
    cmd_up --build --detach
  fi

  sep
  info "ForgeGPU smoke test"
  sep

  # 1. Wait for API
  wait_for_api "${timeout}"

  # 2. Optional workers check
  if [[ "${check_workers}" == "true" ]]; then
    info "Worker states:"
    cmd_workers
  fi

  # 3. Build weight list
  declare -a weight_list=()
  if [[ -n "${weights}" ]]; then
    IFS=',' read -ra weight_list <<< "${weights}"
    job_count="${#weight_list[@]}"
  else
    local i
    for (( i=0; i<job_count; i++ )); do
      weight_list+=("${weight}")
    done
  fi

  info "Submitting ${job_count} job(s) with weights: ${weight_list[*]}"
  declare -a job_ids=()
  local start_time
  start_time=$(date +%s)

  local i
  for (( i=0; i<job_count; i++ )); do
    local w="${weight_list[$i]}"
    local prompt="smoke-job-$((i+1))-w${w}"
    info "  Submitting job $((i+1))/${job_count}: prompt='${prompt}' model='${model}' weight=${w}"
    local jid
    jid=$(submit_job "${prompt}" "${model}" "${w}")
    job_ids+=("${jid}")
    ok "  Submitted → ${jid}"
  done

  # 4. Poll all jobs
  sep
  info "Polling ${job_count} job(s) for completion (timeout: ${timeout}s each)..."
  declare -a final_statuses=()
  local all_ok=true

  for (( i=0; i<job_count; i++ )); do
    local jid="${job_ids[$i]}"
    local w="${weight_list[$i]}"
    info "  Polling job $((i+1))/${job_count}: ${jid} (weight=${w})"
    local status
    status=$(poll_job "${jid}" "${timeout}") || true
    final_statuses+=("${status}")
    case "${status}" in
      Completed)
        ok "  Job ${jid} → ${status}" ;;
      Failed)
        warn "  Job ${jid} → ${status} (job failed on server)"; all_ok=false ;;
      Timeout)
        warn "  Job ${jid} → ${status} (exceeded ${timeout}s)"; all_ok=false ;;
      ConnectionError)
        warn "  Job ${jid} → ${status} (could not reach API)"; all_ok=false ;;
      HTTPError:*)
        warn "  Job ${jid} → ${status}"; all_ok=false ;;
      *)
        warn "  Job ${jid} → unknown status '${status}'"; all_ok=false ;;
    esac
  done

  # 5. Summary
  local end_time
  end_time=$(date +%s)
  local elapsed
  elapsed=$(( end_time - start_time ))

  sep
  info "Smoke test summary"
  sep
  printf "  %-38s  %-8s  %-18s\n" "Job ID" "Weight" "Status"
  printf "  %-38s  %-8s  %-18s\n" "------" "------" "------"
  for (( i=0; i<job_count; i++ )); do
    printf "  %-38s  %-8s  %-18s\n" "${job_ids[$i]}" "${weight_list[$i]}" "${final_statuses[$i]}"
  done
  sep
  info "Total elapsed: ${elapsed}s"

  if [[ "${all_ok}" == "true" ]]; then
    ok "All ${job_count} job(s) completed successfully"
  else
    die "One or more jobs did not complete successfully"
  fi
}

# ===========================================================================
# Subcommand: logs
# ===========================================================================
cmd_logs() {
  local service=""
  local follow=false
  local tail=""

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --follow|-f) follow=true ;;
      --tail)      tail="${2:?'--tail requires a value'}"; shift ;;
      api)      service="forgegpu-api" ;;
      kafka)    service="forgegpu-kafka" ;;
      redis)    service="forgegpu-redis" ;;
      postgres) service="forgegpu-postgres" ;;
      forgegpu-api|forgegpu-kafka|forgegpu-redis|forgegpu-postgres) service="$1" ;;
      *) die "Unknown argument for 'logs': $1  (valid services: api, kafka, redis, postgres)" ;;
    esac
    shift
  done

  local log_args=()
  [[ "${follow}" == "true" ]] && log_args+=(-f)
  [[ -n "${tail}" ]]          && log_args+=(--tail "${tail}")
  [[ -n "${service}" ]]       && log_args+=("${service}")

  dc logs "${log_args[@]+"${log_args[@]}"}"
}

# ===========================================================================
# Subcommand: reset
# ===========================================================================
cmd_reset() {
  local do_rebuild=false
  local do_up=false
  local auto_yes=false

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --rebuild)  do_rebuild=true ;;
      --up)       do_up=true ;;
      --yes|-y)   auto_yes=true ;;
      *) die "Unknown flag for 'reset': $1" ;;
    esac
    shift
  done

  warn "This will destroy all container data (volumes)."
  if [[ "${auto_yes}" == "false" ]]; then
    read -r -p "Continue? [y/N] " confirm
    if [[ "${confirm}" != "y" && "${confirm}" != "Y" ]]; then
      info "Aborted."
      exit 0
    fi
  else
    info "Skipping confirmation (--yes)"
  fi

  info "Resetting ForgeGPU stack (down -v)..."
  dc down -v
  ok "Stack and volumes removed"

  if [[ "${do_rebuild}" == "true" ]]; then
    ensure_env
    info "Rebuilding images..."
    dc build
    ok "Images rebuilt"
  fi

  if [[ "${do_up}" == "true" ]]; then
    local up_flags=()
    [[ "${do_rebuild}" == "true" ]] && up_flags+=(--build)
    cmd_up "${up_flags[@]+"${up_flags[@]}"}" --detach
  fi
}

# ===========================================================================
# Subcommand: status
# ===========================================================================
cmd_status() {
  info "Container status:"
  dc ps
}

# ===========================================================================
# Subcommand: job
# ===========================================================================
cmd_job() {
  local job_id="${1:-}"
  [[ -z "${job_id}" ]] && die "Usage: forgegpu.sh job <id>"
  info "Fetching job ${job_id}..."
  local response
  response=$(http_get "${API_BASE}/jobs/${job_id}")
  printf '%s\n' "${response}" | maybe_jq
}

# ===========================================================================
# Subcommand: submit
# ===========================================================================
cmd_submit() {
  local prompt="test-job"
  local model="${DEFAULT_MODEL}"
  local weight="${DEFAULT_WEIGHT}"

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --prompt) prompt="${2:?'--prompt requires a value'}"; shift ;;
      --model)  model="${2:?'--model requires a value'}"; shift ;;
      --weight) weight="${2:?'--weight requires a value'}"; shift ;;
      *) die "Unknown flag for 'submit': $1" ;;
    esac
    shift
  done

  info "Submitting job: prompt='${prompt}' model='${model}' weight=${weight}"
  local jid
  jid=$(submit_job "${prompt}" "${model}" "${weight}")
  ok "Job submitted → ${jid}"
  info "Poll with: ./scripts/forgegpu.sh job ${jid}"
}

# ===========================================================================
# Subcommand: metrics
# ===========================================================================
cmd_metrics() {
  info "Fetching metrics from ${API_BASE}/metrics..."
  local response
  response=$(http_get "${API_BASE}/metrics")
  printf '%s\n' "${response}" | maybe_jq
}

# ===========================================================================
# Subcommand: topics
# ===========================================================================
cmd_topics() {
  info "Listing Kafka ingress topics..."
  dc exec -T forgegpu-kafka rpk topic list
}

# ===========================================================================
# Subcommand: dashboard
# ===========================================================================
cmd_dashboard() {
  local url="${API_BASE}/dashboard/"
  info "Dashboard URL: ${url}"
  if command -v open >/dev/null 2>&1; then
    open "${url}" >/dev/null 2>&1 || true
  fi
}

# ===========================================================================
# Subcommand: test
# ===========================================================================
cmd_test() {
  local suite="${1:-all}"
  shift || true

  case "${suite}" in
    unit)
      info "Running ForgeGPU unit tests..."
      dotnet test "${REPO_ROOT}/tests/ForgeGPU.UnitTests/ForgeGPU.UnitTests.csproj" "$@"
      ;;
    integration)
      info "Running ForgeGPU integration tests..."
      dotnet test "${REPO_ROOT}/tests/ForgeGPU.IntegrationTests/ForgeGPU.IntegrationTests.csproj" "$@"
      ;;
    all)
      info "Running all ForgeGPU tests..."
      dotnet test "${REPO_ROOT}/tests/ForgeGPU.UnitTests/ForgeGPU.UnitTests.csproj" "$@"
      dotnet test "${REPO_ROOT}/tests/ForgeGPU.IntegrationTests/ForgeGPU.IntegrationTests.csproj" "$@"
      ;;
    *)
      die "Usage: forgegpu.sh test <unit|integration|all> [dotnet test args]"
      ;;
  esac
}

# ===========================================================================
# Subcommand: load
# ===========================================================================
cmd_load() {
  local scenario="${1:-}"
  [[ -z "${scenario}" ]] && die "Usage: forgegpu.sh load <basic|batch|constrained|reliability> [flags]"
  shift || true

  local script_file=""
  case "${scenario}" in
    basic)        script_file="${REPO_ROOT}/load-tests/submit-and-poll.js" ;;
    batch)        script_file="${REPO_ROOT}/load-tests/batch-heavy.js" ;;
    constrained)  script_file="${REPO_ROOT}/load-tests/constrained-capacity.js" ;;
    reliability)  script_file="${REPO_ROOT}/load-tests/reliability-mix.js" ;;
    *) die "Unknown load scenario: ${scenario}" ;;
  esac

  local do_build=false
  local vus=""
  local iterations=""
  local max_duration=""
  local model=""
  local required_memory=""
  local weight=""
  local poll_interval=""
  local poll_timeout=""

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --build)            do_build=true ;;
      --vus)              vus="${2:?'--vus requires a value'}"; shift ;;
      --iterations)       iterations="${2:?'--iterations requires a value'}"; shift ;;
      --max-duration)     max_duration="${2:?'--max-duration requires a value'}"; shift ;;
      --model)            model="${2:?'--model requires a value'}"; shift ;;
      --required-memory)  required_memory="${2:?'--required-memory requires a value'}"; shift ;;
      --weight)           weight="${2:?'--weight requires a value'}"; shift ;;
      --poll-interval-ms) poll_interval="${2:?'--poll-interval-ms requires a value'}"; shift ;;
      --poll-timeout-ms)  poll_timeout="${2:?'--poll-timeout-ms requires a value'}"; shift ;;
      *) die "Unknown flag for 'load': $1" ;;
    esac
    shift
  done

  if [[ "${do_build}" == "true" ]]; then
    cmd_up --build --detach
  fi

  wait_for_api "${DEFAULT_TIMEOUT}"

  info "Running k6 scenario '${scenario}'..."
  [[ "${HAS_K6}" == "true" ]] || warn "Local k6 not found; using grafana/k6 container fallback"

  (
    export API_BASE_URL="${API_BASE}"
    [[ -n "${vus}" ]] && export VUS="${vus}"
    [[ -n "${iterations}" ]] && export ITERATIONS="${iterations}"
    [[ -n "${max_duration}" ]] && export MAX_DURATION="${max_duration}"
    [[ -n "${model}" ]] && export MODEL="${model}"
    [[ -n "${required_memory}" ]] && export REQUIRED_MEMORY_MB="${required_memory}"
    [[ -n "${weight}" ]] && export WEIGHT="${weight}"
    [[ -n "${poll_interval}" ]] && export POLL_INTERVAL_MS="${poll_interval}"
    [[ -n "${poll_timeout}" ]] && export POLL_TIMEOUT_MS="${poll_timeout}"
    run_k6_script "${script_file}"
  )
}

# ===========================================================================
# Usage / help
# ===========================================================================
cmd_help() {
  cat <<EOF

${BOLD}forgegpu.sh${RESET} — local operational entrypoint for ForgeGPU

${BOLD}USAGE${RESET}
  ./scripts/forgegpu.sh <command> [flags]

${BOLD}COMMANDS${RESET}

  ${CYAN}up${RESET} [--build] [--detach]
      Start the full stack with docker compose (foreground by default).
      --build   rebuild images before starting
      --detach  run containers in the background

  ${CYAN}down${RESET} [--volumes]
      Stop the stack.
      --volumes  also remove named volumes (destroys Postgres data)

  ${CYAN}build${RESET} [--docker]
      Run dotnet build on ForgeGPU.sln.
      --docker  also build docker compose images

  ${CYAN}health${RESET}
      Verify the API health endpoint. Exits non-zero if unhealthy.

  ${CYAN}workers${RESET}
      Call GET /workers and print the result.

  ${CYAN}metrics${RESET}
      Call GET /metrics and print the result.

  ${CYAN}topics${RESET}
      List Kafka ingress topics from the local Redpanda runtime.

  ${CYAN}dashboard${RESET}
      Print the operator dashboard URL and open it when supported locally.

  ${CYAN}test${RESET} <unit|integration|all> [dotnet test args]
      Run ForgeGPU automated tests.
      unit         deterministic unit tests only
      integration  HTTP/runtime integration tests
      all          run both suites in sequence

  ${CYAN}smoke${RESET} [options]
      End-to-end smoke test: wait for API → submit jobs → poll to completion → summary.
      --build             build and start the stack detached first, then run the test
      --jobs <n>          number of jobs to submit (default: ${DEFAULT_SMOKE_JOBS})
      --weight <n>        weight applied to all jobs (default: ${DEFAULT_WEIGHT})
      --weights "a,b,c"   comma-separated per-job weights (sets job count automatically)
      --model <name>      inference model (default: ${DEFAULT_MODEL})
      --timeout <s>       per-job poll timeout in seconds (default: ${DEFAULT_TIMEOUT})
      --workers           also print GET /workers during the test

  ${CYAN}logs${RESET} [service] [--follow] [--tail <n>]
      Show docker compose logs. Service shorthand: api | kafka | redis | postgres
      --follow  stream logs (Ctrl+C to stop)
      --tail n  show last n lines only

  ${CYAN}reset${RESET} [--yes] [--rebuild] [--up]
      Destructive reset: docker compose down -v (removes all data volumes).
      --yes      skip interactive confirmation (for scripted flows)
      --rebuild  rebuild images after removing volumes
      --up       start the stack after reset (combines with --rebuild if given)

  ${CYAN}status${RESET}
      Show docker compose ps.

  ${CYAN}job${RESET} <id>
      Fetch GET /jobs/{id} and print result.

  ${CYAN}submit${RESET} [--prompt <text>] [--model <name>] [--weight <n>]
      Submit a single job and print the returned job id.
      Prompt and model values are JSON-safe (supports quotes, backslashes, etc.)

  ${CYAN}load${RESET} <basic|batch|constrained|reliability> [options]
      Run k6-based load scenarios against the current ForgeGPU stack.
      --build                 build and start the stack detached first
      --vus <n>               override virtual users
      --iterations <n>        override total iterations
      --max-duration <dur>    override k6 max duration (for example 3m)
      --model <name>          override model
      --required-memory <mb>  override requiredMemoryMb
      --weight <n>            override job weight
      --poll-interval-ms <n>  override polling interval
      --poll-timeout-ms <n>   override polling timeout

  ${CYAN}help${RESET}
      Show this message.

${BOLD}ENVIRONMENT${RESET}
  FORGEGPU_API_BASE   Override API base URL (default: http://localhost:8080)
  NO_COLOR            Set to disable colored output

${BOLD}DEPENDENCIES${RESET}
  Required: curl, docker compose, dotnet
  Recommended: jq or python3 (for safe JSON handling — at least one should be present)
  Optional: k6 (if absent, load tests run via grafana/k6 docker image)
  If neither jq nor python3 is available, JSON payload generation will fail.
  JSON response parsing falls back to grep/cut if both are absent.

${BOLD}EXAMPLES${RESET}
  ./scripts/forgegpu.sh up --build
  ./scripts/forgegpu.sh health
  ./scripts/forgegpu.sh topics
  ./scripts/forgegpu.sh dashboard
  ./scripts/forgegpu.sh test unit
  ./scripts/forgegpu.sh test integration
  ./scripts/forgegpu.sh smoke --jobs 5 --weights "50,100,300,900,100"
  ./scripts/forgegpu.sh submit --prompt 'hello "world"' --weight 200
  ./scripts/forgegpu.sh load basic --vus 6 --iterations 24
  ./scripts/forgegpu.sh load batch --build --iterations 32 --required-memory 1024
  ./scripts/forgegpu.sh logs api --follow
  ./scripts/forgegpu.sh reset --yes --rebuild --up
  ./scripts/forgegpu.sh down

EOF
}

# ===========================================================================
# Dispatch
# ===========================================================================
COMMAND="${1:-help}"
shift || true

case "${COMMAND}" in
  up)       cmd_up      "$@" ;;
  down)     cmd_down    "$@" ;;
  build)    cmd_build   "$@" ;;
  health)   cmd_health  "$@" ;;
  workers)  cmd_workers "$@" ;;
  metrics)  cmd_metrics "$@" ;;
  topics)   cmd_topics  "$@" ;;
  dashboard) cmd_dashboard "$@" ;;
  test)     cmd_test    "$@" ;;
  smoke)    cmd_smoke   "$@" ;;
  logs)     cmd_logs    "$@" ;;
  reset)    cmd_reset   "$@" ;;
  status)   cmd_status  "$@" ;;
  job)      cmd_job     "$@" ;;
  submit)   cmd_submit  "$@" ;;
  load)     cmd_load    "$@" ;;
  help|--help|-h) cmd_help ;;
  *) die "Unknown command: '${COMMAND}'.  Run './scripts/forgegpu.sh help' for usage." ;;
esac
