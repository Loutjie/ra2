#!/bin/bash
# Launch the game straight into a skirmish on every map in mods/ra2/maps and
# report, per map, whether the world loaded or crashed (OpenRA/ra2 #826).
#
# Usage: tools/maps/launch_all_maps.sh [-t SECONDS] [map-dir-name ...]
#   -t SECONDS  how long to let each map run before closing it (default 25)
#   map names   only these maps, e.g. `the-alamo` (default: all)
# Env: LOGS=<dir> overrides the OpenRA Logs directory to inspect.
#
# Needs a built engine (`make`). Opens a game window per map and kills it
# after the timeout; the engine has no auto-quit.
#
# Result per map:
#   LOADED       no exception log, and perf.log shows LoadComplete (world loaded)
#   CRASH        a new exception-*.log appeared (first line shown)
#   UNCONFIRMED  no crash, but no load evidence (e.g. stuck, or timeout too short)
#   EXITED       launcher exited early with no exception log (output tail shown)
# Exit status is non-zero unless every map is LOADED.

TIMEOUT=25
while getopts "t:" opt; do
	case "$opt" in
		t) TIMEOUT="$OPTARG" ;;
		*) echo "usage: $0 [-t SECONDS] [map ...]" >&2; exit 2 ;;
	esac
done
shift $((OPTIND - 1))

ROOT=$(cd "$(dirname "$0")/../.." && pwd)

# Log directories per Platform.InitializeSupportDir; the engine uses one of them.
if [ -n "$LOGS" ]; then
	LOG_DIRS=("$LOGS")
else
	case "$(uname -s)" in
		Darwin) LOG_DIRS=("$HOME/Library/Application Support/OpenRA/Logs") ;;
		*) LOG_DIRS=("${XDG_CONFIG_HOME:-$HOME/.config}/openra/Logs" "$HOME/.openra/Logs") ;;
	esac
	LOG_DIRS+=("$ROOT/engine/Support/Logs" "$ROOT/Support/Logs")
fi

existing_dirs() {
	for d in "${LOG_DIRS[@]}"; do [ -d "$d" ] && printf '%s\n' "$d"; done
}

if [ $# -gt 0 ]; then
	MAPS=("$@")
else
	MAPS=()
	for d in "$ROOT"/mods/ra2/maps/*/; do MAPS+=("$(basename "$d")"); done
fi

PID=""
OUT=""
MARKER=""
stop_game() {
	[ -n "$PID" ] || return 0
	kill -0 "$PID" 2>/dev/null || return 0
	pkill -P "$PID" 2>/dev/null
	kill "$PID" 2>/dev/null
	for _ in 1 2 3 4 5; do
		kill -0 "$PID" 2>/dev/null || break
		sleep 1
	done
	if kill -0 "$PID" 2>/dev/null; then
		pkill -9 -P "$PID" 2>/dev/null
		kill -9 "$PID" 2>/dev/null
	fi
	wait "$PID" 2>/dev/null
}
cleanup() {
	stop_game
	[ -n "$OUT" ] && rm -f "$OUT"
	[ -n "$MARKER" ] && rm -f "$MARKER"
}
trap 'cleanup; exit 130' INT TERM
trap cleanup EXIT

PROBLEMS=()
LOADED=0

for map in "${MAPS[@]}"; do
	if [ ! -f "$ROOT/mods/ra2/maps/$map/map.yaml" ]; then
		echo "SKIP        $map (no such map)"
		continue
	fi
	MARKER=$(mktemp)
	OUT=$(mktemp)
	# Launch.Map accepts the map folder name or its UID and starts a local skirmish.
	"$ROOT/launch-game.sh" Launch.Map="$map" >"$OUT" 2>&1 &
	PID=$!
	i=0
	while [ "$i" -lt "$TIMEOUT" ] && kill -0 "$PID" 2>/dev/null; do
		sleep 1
		i=$((i + 1))
	done
	exited=yes
	if kill -0 "$PID" 2>/dev/null; then
		exited=no
		stop_game
	else
		wait "$PID" 2>/dev/null
	fi
	PID=""

	crash=""
	loaded=""
	while IFS= read -r dir; do
		while IFS= read -r f; do
			[ -n "$f" ] && crash="$crash$f"$'\n'
		done < <(find "$dir" -name 'exception-*.log' -newer "$MARKER" 2>/dev/null)
		# The engine times Game.StartGame's LoadComplete into perf.log; with
		# Launch.Map no shell map is loaded first, so it means the skirmish world loaded.
		if [ -f "$dir/perf.log" ] && [ "$dir/perf.log" -nt "$MARKER" ] && grep -q "LoadComplete" "$dir/perf.log"; then
			loaded=yes
		fi
	done < <(existing_dirs)

	if [ -n "$crash" ]; then
		echo "CRASH       $map"
		printf '%s' "$crash" | while IFS= read -r f; do
			echo "            $f"
			grep -m1 -i "exception" "$f" | sed 's/^/            /'
		done
		PROBLEMS+=("$map")
	elif [ "$exited" = yes ]; then
		echo "EXITED      $map (launcher exited early; output tail:)"
		tail -n 15 "$OUT" | sed 's/^/            /'
		PROBLEMS+=("$map")
	elif [ -n "$loaded" ]; then
		echo "LOADED      $map"
		LOADED=$((LOADED + 1))
	else
		echo "UNCONFIRMED $map (no crash, no load evidence; try a longer -t)"
		PROBLEMS+=("$map")
	fi
	rm -f "$MARKER" "$OUT"
	MARKER=""
	OUT=""
done

echo
echo "$LOADED loaded; not loaded/problems: ${PROBLEMS[*]:-none}"
[ "${#PROBLEMS[@]}" -eq 0 ]
