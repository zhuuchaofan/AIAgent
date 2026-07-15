#!/usr/bin/env bash
set -euo pipefail

PROJECT_ID="copper-affinity-467409-k7"
REGION="us-central1"
API_SERVICE="life-agent-api"
WEB_SERVICE="life-agent-web"
WEB_DOMAIN="https://life.zhuchaofan.com/"
DEFAULT_SINCE="HEAD~1"

TARGET="auto"
SINCE="$DEFAULT_SINCE"
DRY_RUN="false"

usage() {
  cat <<'USAGE'
Usage: scripts/deploy-lifeos.sh [--target auto|api|web|all|none] [--since REF] [--dry-run]

Standard LifeOS Cloud Run deployment entrypoint.

Examples:
  scripts/deploy-lifeos.sh --dry-run
  scripts/deploy-lifeos.sh --target all
  scripts/deploy-lifeos.sh --target auto --since HEAD~1
  scripts/deploy-lifeos.sh --target auto --since <last-deployed-commit>
USAGE
}

log() {
  printf '[lifeos-deploy] %s\n' "$*"
}

fail() {
  printf '[lifeos-deploy] ERROR: %s\n' "$*" >&2
  exit 1
}

run() {
  if [[ "$DRY_RUN" == "true" ]]; then
    printf '[dry-run] %q' "$1"
    shift
    for arg in "$@"; do
      printf ' %q' "$arg"
    done
    printf '\n'
    return 0
  fi

  "$@"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target)
      TARGET="${2:-}"
      shift 2
      ;;
    --since)
      SINCE="${2:-}"
      shift 2
      ;;
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

case "$TARGET" in
  auto|api|web|all|none) ;;
  *) fail "--target must be one of: auto, api, web, all, none" ;;
esac

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

require_clean_worktree() {
  local status
  status="$(git status --short)"
  if [[ -n "$status" ]]; then
    printf '%s\n' "$status" >&2
    fail "Working tree must be clean before deployment."
  fi
}

resolve_auto_target() {
  git rev-parse --verify "$SINCE" >/dev/null 2>&1 || fail "Cannot resolve --since ref: $SINCE"

  local files
  files="$(git diff --name-only "$SINCE"..HEAD)"
  if [[ -z "$files" ]]; then
    printf 'none\n'
    return
  fi

  local needs_api="false"
  local needs_web="false"
  local needs_all="false"

  while IFS= read -r file; do
    [[ -z "$file" ]] && continue
    case "$file" in
      LifeAgent.Api/*|LifeAgent.Api.csproj|LifeAgent.sln)
        needs_api="true"
        ;;
      life-agent-web/*)
        needs_web="true"
        ;;
      docs/*|scripts/*|LifeAgent.Tests/*|*.md|AGENTS.md|CLAUDE.md|firestore.rules|firebase.json|.firebaserc)
        ;;
      *)
        needs_all="true"
        ;;
    esac
  done <<< "$files"

  if [[ "$needs_all" == "true" ]]; then
    printf 'all\n'
  elif [[ "$needs_api" == "true" && "$needs_web" == "true" ]]; then
    printf 'all\n'
  elif [[ "$needs_api" == "true" ]]; then
    printf 'api\n'
  elif [[ "$needs_web" == "true" ]]; then
    printf 'web\n'
  else
    printf 'none\n'
  fi
}

run_preflight_checks() {
  require_clean_worktree
  run git diff --check
  run dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
  run npm run lint --prefix life-agent-web
  run npm run build --prefix life-agent-web
}

describe_service() {
  local service="$1"
  gcloud run services describe "$service" \
    --region "$REGION" \
    --project "$PROJECT_ID" \
    --format='value(status.url)'
}

assert_web_api_base_matches_current_api() {
  local api_url
  api_url="$(describe_service "$API_SERVICE")"
  [[ -n "$api_url" ]] || fail "Could not read current API Cloud Run URL."

  local env_file="life-agent-web/.env.production"
  [[ -f "$env_file" ]] || fail "Missing $env_file"

  local configured_api_url
  configured_api_url="$(grep -E '^API_BASE_URL=' "$env_file" | tail -n 1 | cut -d= -f2- || true)"
  [[ -n "$configured_api_url" ]] || fail "$env_file does not define API_BASE_URL"

  if [[ "$configured_api_url" != "$api_url" ]]; then
    fail "life-agent-web/.env.production API_BASE_URL ($configured_api_url) does not match current API URL ($api_url). Refusing to deploy Web without an explicit env update."
  fi
}

deploy_api() {
  log "Deploying API service: $API_SERVICE"
  run gcloud run deploy "$API_SERVICE" \
    --source ./LifeAgent.Api \
    --region "$REGION" \
    --project "$PROJECT_ID" \
    --allow-unauthenticated
}

deploy_web() {
  log "Deploying Web service: $WEB_SERVICE"
  if [[ "$DRY_RUN" != "true" ]]; then
    assert_web_api_base_matches_current_api
  else
    log "Would verify life-agent-web/.env.production API_BASE_URL against the current API Cloud Run URL."
  fi
  (cd life-agent-web && run ./deploy.sh)
}

print_revision_status() {
  local service="$1"
  run gcloud run services describe "$service" \
    --region "$REGION" \
    --project "$PROJECT_ID" \
    --format='value(status.latestReadyRevisionName,status.traffic)'
}

post_deploy_checks() {
  log "Checking deployed service revisions."
  print_revision_status "$API_SERVICE"
  print_revision_status "$WEB_SERVICE"

  local api_url
  if [[ "$DRY_RUN" == "true" ]]; then
    log "Would fetch API URL, /health, Web domain, and recent Cloud Run error logs."
    return
  fi

  api_url="$(describe_service "$API_SERVICE")"
  [[ -n "$api_url" ]] || fail "Could not read API service URL."

  log "Checking API health: $api_url/health"
  curl -fsS "$api_url/health" | grep -qi 'healthy' || fail "API /health did not return healthy."

  log "Checking Web custom domain: $WEB_DOMAIN"
  curl -fsS -o /dev/null "$WEB_DOMAIN" || fail "Web custom domain is not reachable."

  log "Checking recent Cloud Run ERROR/500 logs."
  gcloud logging read \
    'resource.type="cloud_run_revision" AND (severity>=ERROR OR httpRequest.status>=500)' \
    --project "$PROJECT_ID" \
    --freshness=15m \
    --limit=20 \
    --format='value(timestamp,resource.labels.service_name,severity,httpRequest.status,textPayload)' || true
}

resolved_target="$TARGET"
if [[ "$TARGET" == "auto" ]]; then
  resolved_target="$(resolve_auto_target)"
fi

log "target=$TARGET resolved=$resolved_target since=$SINCE dry_run=$DRY_RUN"

if [[ "$resolved_target" == "none" ]]; then
  require_clean_worktree
  log "No API or Web deployment is needed for the selected change range."
  exit 0
fi

run_preflight_checks

case "$resolved_target" in
  api)
    deploy_api
    ;;
  web)
    deploy_web
    ;;
  all)
    deploy_api
    deploy_web
    ;;
  *)
    fail "Unexpected resolved target: $resolved_target"
    ;;
esac

post_deploy_checks
log "Deployment flow completed."
