--[[
	Missile Testbed (spike/voxel-missile).

	On WorldLoaded this script pre-builds a play-test setup for the V3 Launcher
	and Dreadnought missile spike, so nobody has to build a base first:

	* Multi0 (the tester) gets lots of cash, a Soviet base near its spawn,
	  a Naval Yard and 2 Dreadnoughts on the nearest water, 3 V3 Launchers on
	  the land between the spawn and the water, and 2 flak tracks plus 2 flak
	  cannons of its own.
	* A cluster of enemy target buildings and enemy anti-air (Patriot, flak
	  cannon, flak track, IFV) is placed within V3 and Dreadnought range of the
	  tester's launchers (11-14 cells from the V3s, 10-22 cells from the
	  Dreadnoughts). The two map spawns are about 66 cells apart, far beyond
	  missile range, so the targets are not put at the Multi1 spawn.
	  They are owned by the map's hostile "Creeps" player rather than by
	  Multi1: a bot in the Multi1 slot would otherwise recruit the anti-air
	  into its attack squads and drive it away. Creeps is hostile to both
	  Multi0 and Multi1 and has no AI.

	Every cell is chosen at runtime by searching outward from an anchor cell
	for valid terrain, so nothing here depends on hand-picked coordinates
	except the map bounds below.
]]

TesterName = "Multi0"
EnemyName = "Multi1"
TargetOwnerName = "Creeps"
TesterCash = 100000

-- Map bounds in map (MPos) coordinates, from map.yaml "Bounds: 2,7,36,148".
-- Right and bottom are exclusive.
BoundsLeft = 2
BoundsTop = 7
BoundsRight = 38
BoundsBottom = 155

-- Terrain types a building or ground unit may stand on (Building.TerrainTypes
-- in rules/defaults.yaml), and the type ships and the Naval Yard need.
LandTypes = { Clear = true, Rough = true, Road = true, DirtRoad = true }
WaterTypes = { Water = true }

-- Firing bands, in cells. V3Launcher: range 18, min 5. DredLauncher: range 25, min 8.
V3BandMin = 11
V3BandMax = 14
DredBandMin = 10
DredBandMax = 22

-- Keep enemy actors at least this far from the tester's launchers and base,
-- so enemy flak tracks and IFVs do not start a ground fight at once.
EnemyStandoff = 10

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
MaxSearchRadius = 45
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

local function farFromTester(cx, cy)
	for _, p in ipairs(TesterSpots) do
		if dist(cx, cy, p.x, p.y) < EnemyStandoff then
			return false
		end
	end

	return true
end

local function report()
	local summary = "Missile testbed: placed " .. #Placed .. " actors."
	if #Failures == 0 then
		Media.DisplayMessage(summary .. " Everything was placed.", "Testbed")
	else
		Media.DisplayMessage(summary .. " Could not place " .. #Failures .. ":", "Testbed")
		for _, f in ipairs(Failures) do
			Media.DisplayMessage("  - " .. f, "Testbed")
		end
	end
end

local function setUp()
	local tester = Player.GetPlayer(TesterName)
	if tester == nil then
		Media.DisplayMessage("Missile testbed: slot " .. TesterName .. " (the first slot) is empty, nothing was placed.", "Testbed")
		return
	end

	local enemy = Player.GetPlayer(EnemyName)
	local targetOwner = Player.GetPlayer(TargetOwnerName) or enemy
	if targetOwner == nil then
		fail("enemy targets (no " .. TargetOwnerName .. " or " .. EnemyName .. " player)")
	end

	pcall(function() tester.Cash = TesterCash end)

	local home = tester.HomeLocation
	local hx = home.X
	local hy = home.Y

	-- Keep the spawn cells clear for the mod's normal starting MCV and units.
	exclude(hx, hy, 3)
	local ex = nil
	local ey = nil
	if enemy ~= nil then
		local eh = enemy.HomeLocation
		ex = eh.X
		ey = eh.Y
		exclude(ex, ey, 4)
	end

	TesterSpots[#TesterSpots + 1] = { x = hx, y = hy }

	local land = { types = LandTypes, flat = false, maxR = 30 }
	local landFlat = { types = LandTypes, flat = true, maxR = 30, fallback = land }
	local water = { types = WaterTypes, flat = false, maxR = 45 }

	-- 1. Naval: nearest water, Naval Yard, two Dreadnoughts.
	local wx, wy = findSpot(hx, hy, 1, 1, water)
	local dreds = { }
	if wx == nil then
		fail("nayard and 2 dred (no water within " .. water.maxR .. " cells of the spawn)")
	else
		local _, yx, yy = place("nayard", tester, wx, wy, 4, 4, water, 1)
		if yx ~= nil then
			TesterSpots[#TesterSpots + 1] = { x = yx, y = yy }
		end

		for _ = 1, 2 do
			-- Margin 2 keeps the ships apart and off the yard.
			local a, dx, dy = place("dred", tester, wx, wy, 1, 1, water, 2)
			if a ~= nil then
				dreds[#dreds + 1] = { x = dx, y = dy }
				TesterSpots[#TesterSpots + 1] = { x = dx, y = dy }
			end
		end
	end

	-- 2. V3 Launchers on land between the spawn and the water.
	local vax = hx
	local vay = hy
	if wx ~= nil then
		vax = math.floor(hx + (wx - hx) * 0.6 + 0.5)
		vay = math.floor(hy + (wy - hy) * 0.6 + 0.5)
	end

	local v3s = { }
	for _ = 1, 3 do
		local a, x, y = place("v3", tester, vax, vay, 1, 1, land, 1)
		if a ~= nil then
			v3s[#v3s + 1] = { x = x, y = y }
			TesterSpots[#TesterSpots + 1] = { x = x, y = y }
		end
	end

	-- 3. The tester's own anti-air, next to the launchers.
	local fx, fy = centroid(v3s)
	if fx == nil then
		fx = hx
		fy = hy
	end

	for _, t in ipairs({ "htk", "htk", "naflak", "naflak" }) do
		local a, x, y = place(t, tester, fx, fy, 1, 1, land, 1)
		if a ~= nil then
			TesterSpots[#TesterSpots + 1] = { x = x, y = y }
		end
	end

	-- 4. Enemy targets and anti-air within range of the launchers.
	local dcx, dcy = centroid(dreds)
	local function inBand(cx, cy, v3Min, v3Max, useDred)
		local d = dist(cx, cy, fx, fy)
		if d < v3Min or d > v3Max then
			return false
		end

		if useDred and dcx ~= nil then
			local dd = dist(cx, cy, dcx, dcy)
			if dd < DredBandMin or dd > DredBandMax then
				return false
			end
		end

		return farFromTester(cx, cy)
	end

	-- Anchor: 13 cells from the launchers, towards the water (so missiles
	-- fly over the lake), else towards the Multi1 spawn.
	local dirx = nil
	local diry = nil
	if wx ~= nil and (wx ~= fx or wy ~= fy) then
		dirx = wx - fx
		diry = wy - fy
	elseif ex ~= nil then
		dirx = ex - fx
		diry = ey - fy
	else
		dirx = 1
		diry = 1
	end

	local len = math.sqrt(dirx * dirx + diry * diry)
	local tax = math.floor(fx + dirx / len * 13 + 0.5)
	local tay = math.floor(fy + diry / len * 13 + 0.5)

	-- Keep the cluster within 60 degrees of that direction, so it sits on one
	-- side of the launchers instead of in a ring around them.
	local ux = dirx / len
	local uy = diry / len
	local function inSector(cx, cy)
		local vx = cx - fx
		local vy = cy - fy
		local vlen = math.sqrt(vx * vx + vy * vy)
		return vlen > 0 and (vx * ux + vy * uy) / vlen >= 0.5
	end

	local strict = function(cx, cy) return inSector(cx, cy) and inBand(cx, cy, V3BandMin, V3BandMax, true) end
	local relaxed = function(cx, cy) return inBand(cx, cy, 8, 17, false) end
	local enemyLand = {
		types = LandTypes, flat = true, maxR = 30, accept = strict,
		fallback = { types = LandTypes, flat = false, maxR = 30, accept = relaxed }
	}

	local enemySpots = { }
	local targetSpots = { }
	if targetOwner ~= nil then
		local buildings = {
			{ "gacnst", 4, 4 }, { "garefn", 4, 3 }, { "gapile", 3, 2 },
			{ "gapowr", 2, 2 }, { "gapowr", 2, 2 }, { "gapowr", 2, 2 }
		}

		for _, b in ipairs(buildings) do
			local a, x, y = place(b[1], targetOwner, tax, tay, b[2], b[3], enemyLand, 1)
			if a ~= nil then
				enemySpots[#enemySpots + 1] = { x = x, y = y }
				targetSpots[#targetSpots + 1] = { x = x, y = y }
			end
		end

		-- Anti-air around the targets, so missiles diving on them pass overhead.
		local tcx, tcy = centroid(targetSpots)
		if tcx == nil then
			tcx = tax
			tcy = tay
		end

		for _, t in ipairs({ "nasam", "nasam", "naflak", "naflak", "htk", "htk", "fv", "fv" }) do
			local a, x, y = place(t, targetOwner, tcx, tcy, 1, 1, enemyLand, 1)
			if a ~= nil then
				enemySpots[#enemySpots + 1] = { x = x, y = y }
			end

			-- Defend: engage what comes into weapon range (missiles included)
			-- but do not drive off to attack the tester's base.
			if a ~= nil and (t == "htk" or t == "fv") then
				setStance(a, "Defend")
			end
		end
	end

	-- 5. The tester's base around the spawn, away from every enemy actor.
	local baseAccept = function(cx, cy)
		for _, p in ipairs(enemySpots) do
			if dist(cx, cy, p.x, p.y) < EnemyStandoff then
				return false
			end
		end

		return true
	end

	local baseFlat = { types = LandTypes, flat = true, maxR = 30, accept = baseAccept,
		fallback = { types = LandTypes, flat = false, maxR = 30, accept = baseAccept } }

	local base = {
		{ "nacnst", 4, 4 }, { "napowr", 3, 2 }, { "napowr", 3, 2 }, { "napowr", 3, 2 },
		{ "narefn", 4, 3 }, { "nahand", 2, 2 }, { "naweap", 5, 3 }
	}

	for _, b in ipairs(base) do
		place(b[1], tester, hx, hy, b[2], b[3], baseFlat, 1)
	end

	-- Point the camera at the launchers.
	pcall(function() Camera.Position = Map.CenterOfCell(CPos.New(fx, fy)) end)

	if #v3s > 0 and #targetSpots > 0 then
		local ecx2, ecy2 = centroid(targetSpots)
		Media.DisplayMessage("Missile testbed: enemy targets are about "
			.. math.floor(dist(fx, fy, ecx2, ecy2) + 0.5) .. " cells from the V3s"
			.. (dcx ~= nil and (" and " .. math.floor(dist(dcx, dcy, ecx2, ecy2) + 0.5) .. " from the Dreadnoughts") or "")
			.. ".", "Testbed")
	end
end

WorldLoaded = function()
	local ok, err = pcall(setUp)
	if not ok then
		Media.DisplayMessage("Missile testbed: setup script error: " .. tostring(err), "Testbed")
	end

	-- Report after a short delay so the messages are not lost behind the load screen.
	Trigger.AfterDelay(DateTime.Seconds(2), report)
end
