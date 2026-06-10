#!/usr/bin/env bash
set -uo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/test-full.sh [options]

Runs OfficeCLI tests from a Debug build without touching Release artifacts.

Options:
  --group NAME       Test group to run. Use "all", "matrix", or a comma list.
                     Default: all
  --configuration C  Build/test configuration. Default: Debug
  --results-dir DIR  Directory for logs and TRX files. Default: TestResults/full-<timestamp>
  --sandbox-dir DIR  Short temp root for HOME/TMPDIR/DOTNET_CLI_HOME.
                     Default: /tmp/officecli-test-<timestamp>
  --no-build         Skip dotnet build and run tests against existing outputs.
  --list             List test cases instead of running them.
  --help             Show this help.

Groups:
  all              One real full-suite run with no filter.
  matrix           Runs all feature slices below, continuing after failures.
  smoke            Upgrade-focused smoke set for aliases, schema, chart, CLI lifecycle.
  core, core-fast  Core parser/query/style unit tests.
  word             DOCX/Word handlers, schema, raw, watermark, tables.
  excel            XLSX/Excel handlers, pivots, tables, formula/display contracts.
  pptx             PPTX/PowerPoint handlers, shapes, effects, media, geometry.
  chart            Chart-specific cross-format tests.
  query-raw-batch  Selector/query/raw/batch/add-part command surfaces.
  resident-watch   CLI subprocess, resident, watch, and batch lifecycle tests.
  showcase         Checked-in showcase baseline validation tests.
  baseline-assets  Alias for showcase.
  fuzz, fuzz-input Fuzzer-style invalid-input and enum/path tests.
  bughunt          Historical bughunt/repro/verify regression files.
  bug-regression   Alias for bughunt.

Examples:
  scripts/test-full.sh
  scripts/test-full.sh --group matrix
  scripts/test-full.sh --group smoke
  scripts/test-full.sh --group word,excel
  scripts/test-full.sh --group all --no-build
USAGE
}

die() {
  echo "error: $*" >&2
  exit 2
}

lowercase() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

repo_root() {
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  cd "$script_dir/.." && pwd
}

resolve_dotnet() {
  if [[ -n "${DOTNET:-}" && -x "${DOTNET:-}" ]]; then
    echo "$DOTNET"
    return
  fi

  if [[ -n "${DOTNET_ROOT:-}" && -x "${DOTNET_ROOT:-}/dotnet" ]]; then
    echo "${DOTNET_ROOT}/dotnet"
    return
  fi

  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return
  fi

  local candidate
  for candidate in \
    /tmp/officecli-dotnet/dotnet \
    "$HOME/.dotnet/dotnet" \
    /opt/homebrew/bin/dotnet \
    /usr/local/share/dotnet/dotnet; do
    if [[ -x "$candidate" ]]; then
      echo "$candidate"
      return
    fi
  done

  die "dotnet not found. Set DOTNET=/path/to/dotnet or install a local SDK."
}

host_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os:$arch" in
    Darwin:arm64) echo "osx-arm64" ;;
    Darwin:x86_64) echo "osx-x64" ;;
    Linux:x86_64) echo "linux-x64" ;;
    Linux:aarch64|Linux:arm64) echo "linux-arm64" ;;
    *) echo "" ;;
  esac
}

resolve_cli_apphost() {
  local root="$1"
  local config="$2"
  local rid
  rid="$(host_rid)"

  local candidates=()
  if [[ -n "$rid" ]]; then
    candidates+=("$root/src/officecli/bin/$config/net10.0/$rid/officecli")
  fi
  candidates+=("$root/src/officecli/bin/$config/net10.0/officecli")

  local path
  for path in "${candidates[@]}"; do
    if [[ -x "$path" ]]; then
      echo "$path"
      return
    fi
  done

  echo ""
}

filter_for_group() {
  case "$1" in
    core|core-fast)
      echo "FullyQualifiedName~OfficeCli.Tests.Core"
      ;;
    smoke)
      echo "FullyQualifiedName~WordRawSetAliasTests|FullyQualifiedName~WordSchemaRegressionTests|FullyQualifiedName~ChartAddPartPlaceholderValidationTests|FullyQualifiedName~ResidentCliLifecycleTests|FullyQualifiedName~WatchCliSessionDiscoveryTests|FullyQualifiedName~WatchSessionTests|FullyQualifiedName~Bug5415|FullyQualifiedName~Excel_Remove_Sheet_ShouldCleanupNamedRanges|FullyQualifiedName~CustomGeometry_Mutations_PreserveSchemaOrder"
      ;;
    word)
      echo "FullyQualifiedName~Word|FullyQualifiedName~Docx|FullyQualifiedName~Watermark|FullyQualifiedName~ParagraphBorder|FullyQualifiedName~TableEnhancement"
      ;;
    excel)
      echo "FullyQualifiedName~Excel|FullyQualifiedName~Xlsx|FullyQualifiedName~Pivot"
      ;;
    pptx)
      echo "FullyQualifiedName~Pptx|FullyQualifiedName~PowerPoint|FullyQualifiedName~Effects"
      ;;
    chart)
      echo "FullyQualifiedName~Chart"
      ;;
    query-raw-batch)
      echo "FullyQualifiedName~AttributeFilterTests|FullyQualifiedName~PptxQueryAttributeFilterTests|FullyQualifiedName~ExcelQueryRowBugTests|FullyQualifiedName~BatchFunctionalTests|FullyQualifiedName~Raw|FullyQualifiedName~Query"
      ;;
    resident-watch)
      echo "FullyQualifiedName~Resident|FullyQualifiedName~Watch|FullyQualifiedName~BatchFunctional"
      ;;
    showcase|baseline-assets)
      echo "FullyQualifiedName~Showcase"
      ;;
    fuzz|fuzz-input)
      echo "FullyQualifiedName~Fuzz|FullyQualifiedName~Fuzzer"
      ;;
    bughunt|bug-regression)
      echo "FullyQualifiedName~BugHunt|FullyQualifiedName~BugRepro|FullyQualifiedName~BugVerify|FullyQualifiedName~BtBug"
      ;;
    *)
      die "unknown test group '$1'"
      ;;
  esac
}

expand_groups() {
  local spec="$1"
  if [[ "$spec" == "matrix" ]]; then
    echo "core-fast smoke word excel pptx chart query-raw-batch resident-watch baseline-assets fuzz-input bug-regression"
    return
  fi

  if [[ "$spec" == "all" ]]; then
    echo "all"
    return
  fi

  echo "$spec" | tr ',' ' '
}

run_dotnet_test() {
  local name="$1"
  local filter="$2"
  local log_file="$RESULTS_DIR/$name.log"
  local trx_name="$name.trx"
  local -a cmd

  cmd=(
    "$DOTNET_EXE" test "$TEST_PROJECT"
    -c "$CONFIGURATION"
    --no-build
    --results-directory "$RESULTS_DIR"
    --logger "trx;LogFileName=$trx_name"
  )

  if [[ "$LIST_ONLY" == "1" ]]; then
    cmd+=(--list-tests)
  fi

  if [[ -n "$filter" ]]; then
    cmd+=(--filter "$filter")
  fi

  {
    echo "## $name"
    echo "filter: ${filter:-<none>}"
    echo "results: $RESULTS_DIR/$trx_name"
    echo "command: ${cmd[*]}"
    echo
  } | tee "$log_file"

  HOME="$TEST_HOME" \
  TMPDIR="$TEST_TMP" \
  OFFICECLI_SKIP_UPDATE=1 \
  OFFICECLI_NO_AUTO_INSTALL=1 \
  OFFICECLI_TEST_CLI="$CLI_APPHOST" \
  "${cmd[@]}" 2>&1 | tee -a "$log_file"
  return "${PIPESTATUS[0]}"
}

append_failed_tests() {
  local name="$1"
  local log_file="$RESULTS_DIR/$name.log"

  awk -v group="$name" '
    /^  Failed / {
      test = $0
      sub(/^  Failed /, "", test)
      sub(/ \[[0-9]+ ms\]$/, "", test)
      print group "\t" test
    }
  ' "$log_file" >> "$FAILED_TESTS_FILE"
}

CONFIGURATION="Debug"
GROUP_SPEC="all"
RESULTS_DIR=""
SANDBOX_DIR=""
NO_BUILD=0
LIST_ONLY=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --group)
      [[ $# -ge 2 ]] || die "--group requires a value"
      GROUP_SPEC="$2"
      shift 2
      ;;
    --configuration|-c)
      [[ $# -ge 2 ]] || die "--configuration requires a value"
      CONFIGURATION="$2"
      shift 2
      ;;
    --results-dir)
      [[ $# -ge 2 ]] || die "--results-dir requires a value"
      RESULTS_DIR="$2"
      shift 2
      ;;
    --sandbox-dir)
      [[ $# -ge 2 ]] || die "--sandbox-dir requires a value"
      SANDBOX_DIR="$2"
      shift 2
      ;;
    --no-build)
      NO_BUILD=1
      shift
      ;;
    --list)
      LIST_ONLY=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      die "unknown option '$1'"
      ;;
  esac
done

if [[ "$(lowercase "$CONFIGURATION")" == "release" ]]; then
  die "Release configuration is intentionally disabled; full-test runs must not touch Release outputs"
fi

REPO_ROOT="$(repo_root)"
DOTNET_EXE="$(resolve_dotnet)"
SOLUTION="$REPO_ROOT/officecli.slnx"
TEST_PROJECT="$REPO_ROOT/tests/OfficeCli.Tests/OfficeCli.Tests.csproj"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
RUN_ID="$TIMESTAMP-$$"

if [[ -z "$RESULTS_DIR" ]]; then
  RESULTS_DIR="$REPO_ROOT/TestResults/full-$RUN_ID"
elif [[ "$RESULTS_DIR" != /* ]]; then
  RESULTS_DIR="$REPO_ROOT/$RESULTS_DIR"
fi

if [[ -z "$SANDBOX_DIR" ]]; then
  SANDBOX_DIR="/tmp/officecli-test-$RUN_ID"
elif [[ "$SANDBOX_DIR" != /* ]]; then
  SANDBOX_DIR="$REPO_ROOT/$SANDBOX_DIR"
fi

mkdir -p "$RESULTS_DIR"
TEST_HOME="$SANDBOX_DIR/home"
TEST_TMP="$SANDBOX_DIR/tmp"
DOTNET_HOME="$SANDBOX_DIR/dotnet-home"
mkdir -p "$TEST_HOME" "$TEST_TMP" "$DOTNET_HOME"

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_HOME="$DOTNET_HOME"
export OFFICECLI_SKIP_UPDATE=1
export OFFICECLI_NO_AUTO_INSTALL=1

echo "repo: $REPO_ROOT"
echo "dotnet: $DOTNET_EXE"
echo "configuration: $CONFIGURATION"
echo "group: $GROUP_SPEC"
echo "results: $RESULTS_DIR"
echo "sandbox: $SANDBOX_DIR"

{
  echo "repo=$REPO_ROOT"
  echo "head=$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || true)"
  echo "branch=$(git -C "$REPO_ROOT" branch --show-current 2>/dev/null || true)"
  echo "configuration=$CONFIGURATION"
  echo "group=$GROUP_SPEC"
  echo "dotnet=$DOTNET_EXE"
  echo "sandbox=$SANDBOX_DIR"
  echo "home=$TEST_HOME"
  echo "tmpdir=$TEST_TMP"
  echo "officecli_skip_update=$OFFICECLI_SKIP_UPDATE"
  echo "officecli_no_auto_install=$OFFICECLI_NO_AUTO_INSTALL"
  "$DOTNET_EXE" --info
} > "$RESULTS_DIR/environment.txt" 2>&1

if [[ "$NO_BUILD" != "1" ]]; then
  echo
  echo "== build =="
  if ! "$DOTNET_EXE" build "$SOLUTION" -c "$CONFIGURATION"; then
    echo "build failed"
    exit 1
  fi
fi

CLI_APPHOST="$(resolve_cli_apphost "$REPO_ROOT" "$CONFIGURATION")"
if [[ -z "$CLI_APPHOST" ]]; then
  die "no $CONFIGURATION officecli apphost found after build"
fi

echo "test cli: $CLI_APPHOST"

FAILURES=0
SUMMARY_FILE="$RESULTS_DIR/summary.tsv"
FAILED_TESTS_FILE="$RESULTS_DIR/failed-tests.tsv"
printf "group\tstatus\tlog\ttrx\n" > "$SUMMARY_FILE"
printf "group\ttest\n" > "$FAILED_TESTS_FILE"

for group in $(expand_groups "$GROUP_SPEC"); do
  if [[ "$group" == "all" ]]; then
    filter=""
  else
    filter="$(filter_for_group "$group")"
  fi

  echo
  echo "== test: $group =="
  if run_dotnet_test "$group" "$filter"; then
    printf "%s\tPASS\t%s\t%s\n" "$group" "$RESULTS_DIR/$group.log" "$RESULTS_DIR/$group.trx" >> "$SUMMARY_FILE"
  else
    status=$?
    FAILURES=$((FAILURES + 1))
    append_failed_tests "$group"
    printf "%s\tFAIL(%s)\t%s\t%s\n" "$group" "$status" "$RESULTS_DIR/$group.log" "$RESULTS_DIR/$group.trx" >> "$SUMMARY_FILE"
  fi
done

echo
echo "== summary =="
cat "$SUMMARY_FILE"

if [[ "$FAILURES" -gt 0 ]]; then
  echo
  if [[ "$(wc -l < "$FAILED_TESTS_FILE")" -gt 1 ]]; then
    echo "failed tests: $FAILED_TESTS_FILE"
  fi
  echo "$FAILURES test group(s) failed. See $RESULTS_DIR"
  exit 1
fi

echo
echo "All requested test group(s) passed. See $RESULTS_DIR"
