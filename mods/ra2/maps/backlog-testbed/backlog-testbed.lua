--[[
	Backlog Testbed: one map to play-test the RA2 backlog features.

	On WorldLoaded this script builds, for the tester in the first slot
	(Multi0), a base and a set of stations. Each station holds the tester's
	units for one feature and, where needed, targets owned by the map's
	hostile, AI-less "Creeps" player. Targets hold fire, so nothing fights
	until you give the order. A chat message on load lists every station and
	its cell; use the debug menu's Fast Charge for the superweapons.

	Every cell is chosen at runtime by searching for valid terrain, as in
	missile-testbed.lua, whose placement helpers this script reuses.
	Test-only: this map stays off main.
]]

TesterName = "Multi0"
EnemyName = "Multi1"
TargetOwnerName = "Creeps"
TesterCash = 100000

-- Map bounds in map (MPos) coordinates, from map.yaml "Bounds: 2,7,36,148".
BoundsLeft = 2
BoundsTop = 7
BoundsRight = 38
BoundsBottom = 155

LandTypes = { Clear = true, Rough = true, Road = true, DirtRoad = true }
WaterTypes = { Water = true }

-- Stations keep this many cells apart, and this far from the spawn.
StationSpacing = 12
SpawnClearance = 10

-- Aircraft wave against the Aegis station, every this many seconds.
AirWaveSeconds = 30

Reserved = { }
Excluded = { }
TerrainCache = { }
HeightCache = { }
FreeCache = { }
Placed = { }
Failures = { }
TesterSpots = { }

local function key(x, y)
	return x .. "," .. y
end

local function dist(ax, ay, bx, by)
	local dx = ax - bx
	local dy = ay - by
	return math.sqrt(dx * dx + dy * dy)
end

local function fail(text)
	Failures[#Failures + 1] = text
end

-- RectangularIsometric CPos -> MPos, as in CPos.ToMPos.
local function inMap(x, y)
	if x < y then
		return false
	end

	local v = x + y
	local u = (v - v % 2) / 2 - y

	-- One cell (two rows) of margin inside the bounds.
	return u >= BoundsLeft + 1 and u < BoundsRight - 1 and v >= BoundsTop + 2 and v < BoundsBottom - 2
end

local function terrainAt(x, y)
	local k = key(x, y)
	local t = TerrainCache[k]
	if t ~= nil then
		return t or nil
	end

	t = false
	if inMap(x, y) then
		local ok, result = pcall(function() return Map.TerrainType(CPos.New(x, y)) end)
		if ok and result ~= nil then
			t = result
		end
	end

	TerrainCache[k] = t
	return t or nil
end

local function heightAt(x, y)
	local k = key(x, y)
	local h = HeightCache[k]
	if h ~= nil then
		return h or nil
	end

	h = false
	local ok, result = pcall(function() return Map.CenterOfCell(CPos.New(x, y)).Z end)
	if ok and result ~= nil then
		h = result
	end

	HeightCache[k] = h
	return h or nil
end

-- True if no actor that was in the world when the script ran (map trees,
-- rocks, starting units, ...) stands on the cell.
local function actorFree(x, y)
	local k = key(x, y)
	local free = FreeCache[k]
	if free ~= nil then
		return free
	end

	free = true
	local ok, actors = pcall(function()
		return Map.ActorsInCircle(Map.CenterOfCell(CPos.New(x, y)), WDist.New(700))
	end)

	if ok and actors ~= nil and #actors > 0 then
		free = false
	end

	FreeCache[k] = free
	return free
end

local function reserve(x, y, w, h, margin)
	for i = -margin, w - 1 + margin do
		for j = -margin, h - 1 + margin do
			Reserved[key(x + i, y + j)] = true
		end
	end
end

local function exclude(cx, cy, radius)
	for i = -radius, radius do
		for j = -radius, radius do
			if i * i + j * j <= radius * radius then
				Excluded[key(cx + i, cy + j)] = true
			end
		end
	end
end

-- Offsets sorted by distance (ties broken by dy then dx, so the order is
-- deterministic on every client).
Offsets = { }
MaxSearchRadius = 70
for dx = -MaxSearchRadius, MaxSearchRadius do
	for dy = -MaxSearchRadius, MaxSearchRadius do
		local d2 = dx * dx + dy * dy
		if d2 <= MaxSearchRadius * MaxSearchRadius then
			Offsets[#Offsets + 1] = { dx = dx, dy = dy, d2 = d2 }
		end
	end
end

table.sort(Offsets, function(a, b)
	if a.d2 ~= b.d2 then
		return a.d2 < b.d2
	end

	if a.dy ~= b.dy then
		return a.dy < b.dy
	end

	return a.dx < b.dx
end)

local function footprintOk(x, y, w, h, types, flat)
	local z0 = nil
	for i = 0, w - 1 do
		for j = 0, h - 1 do
			local cx = x + i
			local cy = y + j
			local k = key(cx, cy)
			if Reserved[k] or Excluded[k] then
				return false
			end

			local t = terrainAt(cx, cy)
			if t == nil or not types[t] then
				return false
			end

			if flat then
				local z = heightAt(cx, cy)
				if z == nil then
					return false
				end

				if z0 == nil then
					z0 = z
				elseif z ~= z0 then
					return false
				end
			end

			if not actorFree(cx, cy) then
				return false
			end
		end
	end

	return true
end

-- Search outward from (ax, ay) for a w x h footprint (top-left returned).
-- opts: types, flat, maxR, accept(centreX, centreY) -> bool.
local function findSpot(ax, ay, w, h, opts)
	local maxR = opts.maxR or 30
	local maxD2 = maxR * maxR
	for _, o in ipairs(Offsets) do
		if o.d2 > maxD2 then
			break
		end

		local x = ax + o.dx
		local y = ay + o.dy
		local cx = x + (w - 1) / 2
		local cy = y + (h - 1) / 2
		if (opts.accept == nil or opts.accept(cx, cy)) and footprintOk(x, y, w, h, opts.types, opts.flat) then
			return x, y
		end
	end

	return nil, nil
end

local function create(actorType, owner, x, y)
	local ok, result = pcall(function()
		return Actor.Create(actorType, true, { Owner = owner, Location = CPos.New(x, y) })
	end)

	if ok and result ~= nil then
		Placed[#Placed + 1] = actorType
		return result
	end

	fail(actorType .. " (Actor.Create failed: " .. tostring(result) .. ")")
	return nil
end

-- Place one actor of size w x h. Returns the actor and its centre cell.
local function place(actorType, owner, ax, ay, w, h, opts, margin)
	local x, y = findSpot(ax, ay, w, h, opts)
	if x == nil and opts.fallback ~= nil then
		x, y = findSpot(ax, ay, w, h, opts.fallback)
	end

	if x == nil then
		fail(actorType .. " (no valid cell)")
		return nil
	end

	reserve(x, y, w, h, margin or 1)
	local actor = create(actorType, owner, x, y)
	if actor == nil then
		return nil
	end

	return actor, x + (w - 1) / 2, y + (h - 1) / 2
end

local function setStance(actor, stance)
	if actor ~= nil then
		pcall(function() actor.Stance = stance end)
	end
end

local function centroid(points)
	if #points == 0 then
		return nil, nil
	end

	local sx = 0
	local sy = 0
	for _, p in ipairs(points) do
		sx = sx + p.x
		sy = sy + p.y
	end

	return math.floor(sx / #points + 0.5), math.floor(sy / #points + 0.5)
end

Stations = { }
StationSpots = { }
Messages = { }

local function note(text)
	Messages[#Messages + 1] = text
end

local function farFromStations(cx, cy, spacing)
	for _, p in ipairs(StationSpots) do
		if dist(cx, cy, p.x, p.y) < spacing then
			return false
		end
	end

	return true
end

-- Find a station anchor of the given terrain, away from the spawn and the other stations.
local function stationAnchor(hx, hy, types, maxR)
	local accept = function(cx, cy)
		return dist(cx, cy, hx, hy) >= SpawnClearance and farFromStations(cx, cy, StationSpacing)
	end

	local x, y = findSpot(hx, hy, 1, 1, { types = types, flat = false, maxR = maxR, accept = accept })
	if x == nil then
		-- Relax the spacing before giving up.
		accept = function(cx, cy)
			return dist(cx, cy, hx, hy) >= SpawnClearance and farFromStations(cx, cy, StationSpacing / 2)
		end

		x, y = findSpot(hx, hy, 1, 1, { types = types, flat = false, maxR = maxR, accept = accept })
	end

	if x ~= nil then
		StationSpots[#StationSpots + 1] = { x = x, y = y }
	end

	return x, y
end

local function unit(actorType, owner, ax, ay, opts)
	local a = place(actorType, owner, ax, ay, 1, 1, opts, 0)
	return a
end

local function elite(actor)
	if actor ~= nil then
		pcall(function() actor.GiveLevels(2, true) end)
	end

	return actor
end

local function halfHealth(actor)
	if actor ~= nil then
		pcall(function() actor.Health = math.floor(actor.MaxHealth / 2) end)
	end

	return actor
end

local function holdFire(actor)
	setStance(actor, "HoldFire")
	return actor
end

-- A point d cells from (ax, ay) towards (bx, by).
local function towards(ax, ay, bx, by, d)
	local dx = bx - ax
	local dy = by - ay
	local len = math.sqrt(dx * dx + dy * dy)
	if len == 0 then
		return ax + d, ay
	end

	return math.floor(ax + dx / len * d + 0.5), math.floor(ay + dy / len * d + 0.5)
end

local function report()
	local summary = "Backlog testbed: placed " .. #Placed .. " actors."
	Media.DisplayMessage(summary, "Testbed")
	print(summary)
	for _, m in ipairs(Messages) do
		Media.DisplayMessage(m, "Testbed")
		print(m)
	end

	if #Failures > 0 then
		Media.DisplayMessage("Could not place " .. #Failures .. ":", "Testbed")
		print("Could not place " .. #Failures .. ":")
		for _, f in ipairs(Failures) do
			Media.DisplayMessage("  - " .. f, "Testbed")
			print("  - " .. f)
		end
	end
end

-- The map cell farthest from (x, y), for aircraft to fly in from.
local function farCell(x, y)
	local best = nil
	local bestD = -1
	for u = BoundsLeft + 2, BoundsRight - 3, 3 do
		for v = BoundsTop + 3, BoundsBottom - 4, 6 do
			local cy = (v - v % 2) / 2 - u
			local cx = v - cy
			local d = dist(cx, cy, x, y)
			if inMap(cx, cy) and d > bestD then
				best = { x = cx, y = cy }
				bestD = d
			end
		end
	end

	return best
end

local function setUp()
	local tester = Player.GetPlayer(TesterName)
	if tester == nil then
		print("Backlog testbed: the first slot is empty, nothing was placed.")
		Media.DisplayMessage("Backlog testbed: slot " .. TesterName .. " (the first slot) is empty, nothing was placed.", "Testbed")
		return
	end

	local creeps = Player.GetPlayer(TargetOwnerName)
	if creeps == nil then
		fail("all targets (no " .. TargetOwnerName .. " player)")
	end

	pcall(function() tester.Cash = TesterCash end)

	local home = tester.HomeLocation
	local hx = home.X
	local hy = home.Y
	exclude(hx, hy, 3)
	local enemy = Player.GetPlayer(EnemyName)
	if enemy ~= nil then
		exclude(enemy.HomeLocation.X, enemy.HomeLocation.Y, 4)
	end

	local land = { types = LandTypes, flat = false, maxR = 8 }
	local landFlat = { types = LandTypes, flat = true, maxR = 10, fallback = { types = LandTypes, flat = false, maxR = 12 } }
	local water = { types = WaterTypes, flat = false, maxR = 8 }

	-- Base around the spawn: power, factories, and the Allied Weather Control Device (#17).
	-- Spawn clearance keeps the stations off it.
	StationSpots[#StationSpots + 1] = { x = hx, y = hy }
	local baseOpts = { types = LandTypes, flat = true, maxR = 14, fallback = { types = LandTypes, flat = false, maxR = 16 } }
	for _, b in ipairs({ { "nacnst", 4, 4 }, { "napowr", 3, 2 }, { "napowr", 3, 2 }, { "napowr", 3, 2 }, { "napowr", 3, 2 },
		{ "naweap", 5, 3 }, { "nahand", 2, 2 }, { "naradr", 2, 2 }, { "gaweat", 4, 4 } }) do
		place(b[1], tester, hx, hy, b[2], b[3], baseOpts, 1)
	end

	note("Base at " .. hx .. "," .. hy .. ": build anything to see the sidebar (#14) and the building ghost (#15); "
		.. "fire the Weather Control Device (#17) and watch the storm build up.")

	-- 1. Self-heal (#10): damaged self-healers, one of them elite.
	local x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		for _, t in ipairs({ "apoc", "deso", "harv", "cmin" }) do
			holdFire(halfHealth(unit(t, tester, x, y, land)))
		end

		holdFire(halfHealth(elite(unit("apoc", tester, x, y, land))))
		note("1 Self-heal (#10) at " .. x .. "," .. y .. ": Apocalypse, Desolator, War Miner, Chrono Miner at half health "
			.. "heal to full; the elite Apocalypse heals no faster.")
	else
		fail("station 1 self-heal (no land)")
	end

	-- 2. Iron Curtain (#6): the curtain and an enemy group of infantry and tanks.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		place("nairon", tester, x, y, 3, 3, landFlat, 1)
		if creeps ~= nil then
			local tx, ty = towards(x, y, hx, hy, -6)
			for _, t in ipairs({ "e1", "e1", "e2", "e2", "htnk", "htnk" }) do
				holdFire(unit(t, creeps, tx, ty, land))
			end

			note("2 Iron Curtain (#6) at " .. x .. "," .. y .. ": curtain the enemy group at " .. tx .. "," .. ty
				.. "; infantry die, tanks turn invulnerable.")
		end
	else
		fail("station 2 Iron Curtain (no land)")
	end

	-- 3. Psychic Sensor (#9, #27): enemies attack a power plant near the sensor; a disguised Spy and a Mirage Tank wait.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		place("napsis", tester, x, y, 2, 2, landFlat, 1)
		local px, py = towards(x, y, hx, hy, 6)
		local target = place("napowr", tester, px, py, 3, 2, landFlat, 1)
		if creeps ~= nil then
			local ax, ay = towards(x, y, hx, hy, -7)
			local attackers = { }
			for _, t in ipairs({ "mtnk", "mtnk", "e1", "e1" }) do
				attackers[#attackers + 1] = unit(t, creeps, ax, ay, land)
			end

			local function sendAttackers(t)
				for _, a in ipairs(attackers) do
					if a ~= nil and not a.IsDead and t ~= nil and not t.IsDead then
						pcall(function() a.Attack(t) end)
					end
				end
			end

			-- Rebuild the target when it falls, so the attack lines keep coming.
			local function watch(t)
				if t == nil then
					return
				end

				Trigger.OnKilled(t, function()
					Trigger.AfterDelay(DateTime.Seconds(3), function()
						local nt = place("napowr", tester, px, py, 3, 2, landFlat, 1)
						sendAttackers(nt)
						watch(nt)
					end)
				end)
			end

			Trigger.AfterDelay(DateTime.Seconds(5), function() sendAttackers(target) end)
			watch(target)

			local sx, sy = towards(x, y, hx, hy, -4)
			local spy = holdFire(unit("spy", creeps, sx, sy, land))
			if spy ~= nil then
				pcall(function() spy.DisguiseAsType("e2", tester) end)
			end

			holdFire(unit("mgtk", creeps, sx, sy, land))
			note("3 Psychic Sensor (#9, #27) at " .. x .. "," .. y .. ": red attack lines from the enemies attacking the power plant; "
				.. "the Spy (disguised as your Conscript) and the Mirage Tank near " .. sx .. "," .. sy .. " show their true identity.")
		end
	else
		fail("station 3 Psychic Sensor (no land)")
	end

	-- 4. Tesla bounce (#11): elite Tesla Tanks and Troopers, an infantry cluster out of range.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		for _, t in ipairs({ "ttnk", "ttnk", "shk", "shk", "shk" }) do
			elite(unit(t, tester, x, y, land))
		end

		if creeps ~= nil then
			local tx, ty = towards(x, y, hx, hy, -8)
			for _ = 1, 8 do
				holdFire(unit("e2", creeps, tx, ty, land))
			end

			note("4 Tesla bounce (#11) at " .. x .. "," .. y .. ": order the elite Tesla units onto the infantry at " .. tx .. "," .. ty
				.. "; each bolt jumps to 2 more.")
		end
	else
		fail("station 4 Tesla (no land)")
	end

	-- 6. Crazy Ivan (#7): bombable and non-bombable targets, and a patrolling tank.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		for i = 1, 3 do
			local a = unit("ivan", tester, x, y, land)
			if i == 3 then
				elite(a)
			end
		end

		if creeps ~= nil then
			local tx, ty = towards(x, y, hx, hy, -6)
			for _, t in ipairs({ "e1", "e1", "e2", "e2" }) do
				holdFire(unit(t, creeps, tx, ty, land))
			end

			place("gapowr", creeps, tx, ty, 2, 2, landFlat, 1)
			local tank = holdFire(unit("htnk", creeps, tx, ty, land))
			if tank ~= nil then
				local ex1, ey1 = towards(x, y, hx, hy, -3)
				local ex2, ey2 = towards(x, y, hx, hy, -10)
				pcall(function() tank.Patrol({ CPos.New(ex1, ey1), CPos.New(ex2, ey2) }, true, 25) end)
			end

			note("6 Crazy Ivan (#7) at " .. x .. "," .. y .. ": bomb the patrolling tank (the bomb rides along), the GIs and the power plant; "
				.. "the Conscripts cannot be bombed.")
		end
	else
		fail("station 6 Crazy Ivan (no land)")
	end

	-- 7. Chrono Legionnaire (#5): infantry, a tank and a building to erase.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		unit("cleg", tester, x, y, land)
		unit("cleg", tester, x, y, land)
		elite(unit("cleg", tester, x, y, land))
		if creeps ~= nil then
			local tx, ty = towards(x, y, hx, hy, -7)
			for _, t in ipairs({ "e1", "e1", "e1", "htnk" }) do
				holdFire(unit(t, creeps, tx, ty, land))
			end

			place("gapowr", creeps, tx, ty, 2, 2, landFlat, 1)
			note("7 Chrono Legionnaire (#5) at " .. x .. "," .. y .. ": erase the targets at " .. tx .. "," .. ty
				.. " (blue bar, no wreck); move a Legionnaire away mid-beam to see the target released.")
		end
	else
		fail("station 7 Chrono Legionnaire (no land)")
	end

	-- 9. Prism fragments (#4): Prism Tanks and a group to split into.
	x, y = stationAnchor(hx, hy, LandTypes, 60)
	if x ~= nil then
		unit("sref", tester, x, y, land)
		elite(unit("sref", tester, x, y, land))
		if creeps ~= nil then
			local tx, ty = towards(x, y, hx, hy, -9)
			for _, t in ipairs({ "e2", "e2", "e2", "e2", "e2", "htnk" }) do
				holdFire(unit(t, creeps, tx, ty, land))
			end

			note("9 Prism fragments (#4) at " .. x .. "," .. y .. ": fire the Prism Tanks at the group at " .. tx .. "," .. ty
				.. "; beams split into fragments.")
		end
	else
		fail("station 9 Prism (no land)")
	end

	-- 5. Aegis (#12, #28): normal and elite Aegis Cruisers; aircraft fly in every AirWaveSeconds.
	x, y = stationAnchor(hx, hy, WaterTypes, 70)
	if x ~= nil then
		local aegis = { }
		aegis[1] = unit("aegis", tester, x, y, water)
		aegis[2] = elite(unit("aegis", tester, x, y, water))
		local entry = farCell(x, y)
		if creeps ~= nil and entry ~= nil then
			local function wave()
				local targets = { }
				for _, a in ipairs(aegis) do
					if a ~= nil and not a.IsDead then
						targets[#targets + 1] = a
					end
				end

				if #targets == 0 then
					return
				end

				local ok, planes = pcall(function()
					return Reinforcements.Reinforce(creeps, { "orca", "orca", "orca", "beag" },
						{ CPos.New(entry.x, entry.y), CPos.New(x, y) }, 10)
				end)

				if ok and planes ~= nil then
					for i, p in ipairs(planes) do
						local t = targets[(i - 1) % #targets + 1]
						pcall(function() p.Attack(t) end)
					end
				end

				Trigger.AfterDelay(DateTime.Seconds(AirWaveSeconds), wave)
			end

			Trigger.AfterDelay(DateTime.Seconds(15), wave)
		end

		note("5 Aegis (#12, #28) at " .. x .. "," .. y .. ": aircraft fly in every " .. AirWaveSeconds
			.. " s; missiles spread over them, the elite Aegis reaches further.")
	else
		fail("station 5 Aegis (no water)")
	end

	-- 8. Giant Squid (#8): squids (one damaged, for #10) next to enemy ships.
	x, y = stationAnchor(hx, hy, WaterTypes, 70)
	if x ~= nil then
		unit("sqd", tester, x, y, water)
		halfHealth(unit("sqd", tester, x, y, water))
		if creeps ~= nil then
			local tx, ty = x, y
			for _, t in ipairs({ "dest", "dest", "aegis" }) do
				holdFire(unit(t, creeps, tx + 4, ty, { types = WaterTypes, flat = false, maxR = 10 }))
			end
		end

		note("8 Giant Squid (#8) at " .. x .. "," .. y .. ": order a squid onto a ship; it grabs and holds it. "
			.. "The damaged squid heals (#10).")
	else
		fail("station 8 Giant Squid (no water)")
	end

	note("Main menu (#16) shows before the game; civilian buildings (#20) are urban-only, check them in the map editor.")
	pcall(function() Camera.Position = Map.CenterOfCell(CPos.New(hx, hy)) end)
end

WorldLoaded = function()
	local ok, err = pcall(setUp)
	if not ok then
		print("Backlog testbed: setup script error: " .. tostring(err))
		Media.DisplayMessage("Backlog testbed: setup script error: " .. tostring(err), "Testbed")
	end

	Trigger.AfterDelay(DateTime.Seconds(2), report)
end
