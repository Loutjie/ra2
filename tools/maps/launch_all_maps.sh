#!/bin/sh
# Launch the game straight into a skirmish on every map in mods/ra2/maps and
# report any new exception-*.log per map (OpenRA/ra2 #826 map-load crash check).
#
# Usage: tools/maps/launch_all_maps.sh [-t SECONDS] [map-dir-name ...]
#   -t SECONDS  how long to let each map run before closing it (default 25)
#   map names   only these maps, e.g. `the-alamo` (default: all)
#
# Needs a built engine (`make`). Opens a game window per map and kills it
# after the timeout; a map that crashes while loading exits early.
# The engine has no auto-quit, so the timeout does the quitting.

TIMEOUT=25
while getopts "t:" opt; do
	case "$opt" in
		t) TIMEOUT="$OPTARG" ;;
		*) echo "usage: $0 [-t SECONDS] [map ...]" >&2; exit 2 ;;
	esac
done
shift $((OPTIND - 1))

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
case "$(uname -s)" in
	Darwin) LOGS="$HOME/Library/Application Support/OpenRA/Logs" ;;
	*) LOGS="${XDG_CONFIG_HOME:-$HOME/.config}/openra/Logs" ;;
esac

if [ $# -gt 0 ]; then
	MAPS="$*"
else
	MAPS=$(ls "$ROOT/mods/ra2/maps")
fi

mkdir -p "$LOGS"
FAILED=""
OKAY=0

for map in $MAPS; do
	if [ ! -f "$ROOT/mods/ra2/maps/$map/map.yaml" ]; then
		echo "SKIP $map (no such map)"
		continue
	fi
	marker=$(mktemp)
	# Launch.Map accepts the map folder name or its UID and starts a local skirmish.
	"$ROOT/launch-game.sh" Launch.Map="$map" >/dev/null 2>&1 &
	pid=$!
	i=0
	while [ "$i" -lt "$TIMEOUT" ] && kill -0 "$pid" 2>/dev/null; do
		sleep 1
		i=$((i + 1))
	done
	if kill -0 "$pid" 2>/dev/null; then
		# Still running after the timeout: the map loaded. Close it.
		pkill -P "$pid" 2>/dev/null
		kill "$pid" 2>/dev/null
		wait "$pid" 2>/dev/null
		exited=no
	else
		wait "$pid" 2>/dev/null
		exited=yes
	fi
	new=$(find "$LOGS" -name 'exception-*.log' -newer "$marker" 2>/dev/null)
	rm -f "$marker"
	if [ -n "$new" ]; then
		echo "FAIL $map"
		echo "$new" | while IFS= read -r f; do
			echo "     $f"
			grep -m1 -i "exception" "$f" | sed 's/^/     /'
		done
		FAILED="$FAILED $map"
	elif [ "$exited" = yes ]; then
		echo "WARN $map (game exited early with no exception log; check $LOGS/client.log)"
		FAILED="$FAILED $map"
	else
		echo "OK   $map"
		OKAY=$((OKAY + 1))
	fi
done

echo
echo "$OKAY ok; problems:${FAILED:- none}"
[ -z "$FAILED" ]
