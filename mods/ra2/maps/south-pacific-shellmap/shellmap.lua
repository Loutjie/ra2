--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

-- An endless Allied-versus-Soviet battle behind the main menu.
-- See docs/superpowers/specs/2026-11-18-south-pacific-shellmap-design.md.

-- A side skips its next ground wave while it has more ground units than this.
GroundCap = 30

AlliedGround = { "sref", "mgtk", "mtnk", "fv", "e1" }
SovietGround = { "htnk", "apoc", "ttnk", "shk", "e2", "v3" }

-- The Allied wave is larger: Soviet armour is tougher, and with equal waves the Allies were outnumbered 2:1.
AlliedWave = { "mtnk", "mtnk", "mtnk", "sref", "sref", "mgtk", "fv", "fv", "e1", "e1", "e1", "e1" }
SovietWave = { "htnk", "htnk", "apoc", "ttnk", "shk", "shk", "e2", "e2" }

AlliedFleet = { aegis = 2, dest = 1 }
SovietFleet = { dred = 2 }

-- Number of the player's units of the given types that are alive.
CountAlive = function(player, types)
	return #player.GetActorsByTypes(types)
end

-- Ground units alive for a side: what the wave cap and the soak test watch.
AliveUnits = function(player)
	if player == Allies then
		return CountAlive(Allies, AlliedGround)
	end

	return CountAlive(Soviets, SovietGround)
end

-- A random land cell in the battle zone, so units spread over the strip instead of bunching on one cell.
RandomStripCell = function()
	local tl = BattleZoneTopLeft.Location
	local br = BattleZoneBottomRight.Location
	for _ = 1, 12 do
		local cell = CPos.New(Utils.RandomInteger(tl.X, br.X + 1), Utils.RandomInteger(tl.Y, br.Y + 1))
		local terrain = Map.TerrainType(cell)
		if terrain == "Clear" or terrain == "Rough" or terrain == "Road" or terrain == "DirtRoad" then
			return cell
		end
	end

	return StripCenter.Location
end

AttackStrip = function(units)
	Utils.Do(units, function(unit)
		if not unit.IsDead then
			unit.AttackMove(RandomStripCell())
			Trigger.OnIdle(unit, function(u) u.AttackMove(RandomStripCell()) end)
		end
	end)
end

SendGroundWave = function(player, factory, barracks, wave)
	if AliveUnits(player) <= GroundCap then
		local vehicles = { }
		local infantry = { }
		Utils.Do(wave, function(type)
			if type == "e1" or type == "e2" or type == "shk" then
				infantry[#infantry + 1] = type
			else
				vehicles[#vehicles + 1] = type
			end
		end)

		AttackStrip(Reinforcements.Reinforce(player, vehicles, { factory }, DateTime.Seconds(1)))
		AttackStrip(Reinforcements.Reinforce(player, infantry, { barracks }, 10))
	end
end

AlliedWaves = function()
	SendGroundWave(Allies, AlliedFactoryExit, AlliedBarracksExit, AlliedWave)
	Trigger.AfterDelay(DateTime.Seconds(Utils.RandomInteger(30, 46)), AlliedWaves)
end

SovietWaveCount = 0
SovietWaves = function()
	SovietWaveCount = SovietWaveCount + 1
	local wave = SovietWave
	if SovietWaveCount % 2 == 0 then
		wave = Utils.Concat(SovietWave, { "v3" })
	end

	SendGroundWave(Soviets, SovietFactoryExit, SovietBarracksExit, wave)
	Trigger.AfterDelay(DateTime.Seconds(Utils.RandomInteger(30, 46)), SovietWaves)
end

-- Flies in from the map edge, attacks one Soviet unit on the strip, then carries on out of the far edge
-- along the same line, so a fixed-wing jet never has to reverse or loop back.
-- Entering on a path towards the target spawns it already facing the strip.
Sortie = function(player, type, edge)
	local targets = UnitsInBattleZone(function(a) return a.Owner == Soviets end)
	if #targets == 0 then
		return
	end

	local target = Utils.Random(targets)
	local aim = target.Location
	local exit = Map.ClosestEdgeCell(aim + (aim - edge))
	local aircraft = Reinforcements.Reinforce(player, { type }, { edge, aim })[1]
	aircraft.Attack(target)
	aircraft.Move(exit)
	aircraft.Destroy()
end

AlliedAir = function()
	Sortie(Allies, Utils.Random({ "orca", "beag" }), AlliedAirEdge)
	Trigger.AfterDelay(DateTime.Seconds(Utils.RandomInteger(60, 91)), AlliedAir)
end

KirovRun = function()
	if CountAlive(Soviets, { "zep" }) == 0 then
		local kirov = Reinforcements.Reinforce(Soviets, { "zep" }, { SovietAirEdge, StripCenter.Location })[1]
		kirov.AttackMove(AlliedFactory.Location)
		kirov.Move(SovietAirEdge)
		kirov.Destroy()
	end

	Trigger.AfterDelay(DateTime.Seconds(90), KirovRun)
end

-- Keeps each side's fleet at full strength, fighting at SeaBattle.
TopUpFleets = function()
	for type, count in pairs(AlliedFleet) do
		for _ = CountAlive(Allies, { type }) + 1, count do
			AttackSea(Reinforcements.Reinforce(Allies, { type }, { AlliedYardExit }))
		end
	end

	for type, count in pairs(SovietFleet) do
		for _ = CountAlive(Soviets, { type }) + 1, count do
			AttackSea(Reinforcements.Reinforce(Soviets, { type }, { SovietYardExit }))
		end
	end

	Trigger.AfterDelay(DateTime.Seconds(20), TopUpFleets)
end

AttackSea = function(ships)
	Utils.Do(ships, function(ship)
		ship.AttackMove(SeaBattle.Location)
		Trigger.OnIdle(ship, function(s) s.AttackMove(SeaBattle.Location) end)
	end)
end

-- Each Tesla Trooper keeps zapping its Tesla Coil, which keeps the coil charged.
ChargeTeslaCoils = function()
	for i = 0, 4 do
		local trooper = Map.NamedActor("TeslaTrooper" .. i)
		local coil = Map.NamedActor("SovietTesla" .. i)
		if trooper and coil and not trooper.IsDead then
			trooper.Attack(coil, false, true)
		end
	end
end

-- Superweapons and the camera --------------------------------------------------------------------

SovietTanks = { "htnk", "apoc", "ttnk" }

-- Mobile units of either side inside the battle zone (the land strip between the islands).
UnitsInBattleZone = function(filter)
	local tl = BattleZoneTopLeft.CenterPosition
	local br = BattleZoneBottomRight.CenterPosition
	local minimum = WPos.New(math.min(tl.X, br.X), math.min(tl.Y, br.Y), 0)
	local maximum = WPos.New(math.max(tl.X, br.X), math.max(tl.Y, br.Y), 10240)
	return Map.ActorsInBox(minimum, maximum, function(a)
		return not a.IsDead and a.HasProperty("Move") and (a.Owner == Allies or a.Owner == Soviets) and (filter == nil or filter(a))
	end)
end

-- The battle-zone cell with the most units within 3 cells; the strip's centre when the zone is empty.
-- Only units inside the zone are candidates, so a strike never lands in a base.
StrikeTarget = function()
	local best = nil
	local bestCount = 0
	Utils.Do(UnitsInBattleZone(), function(unit)
		local count = #Map.ActorsInCircle(unit.CenterPosition, WDist.FromCells(3), function(a) return a.HasProperty("Move") end)
		if count > bestCount then
			best = unit.Location
			bestCount = count
		end
	end)

	return best or StripCenter.Location
end

-- Free land cells around a point in the battle zone, for chronoshifted units to land on.
FreeLandCells = function(centre, wanted)
	local cells = { }
	for dx = -3, 3, 2 do
		for dy = -3, 3, 2 do
			local cell = centre + CVec.New(dx, dy)
			local terrain = Map.TerrainType(cell)
			local land = terrain == "Clear" or terrain == "Rough" or terrain == "Road" or terrain == "DirtRoad"
			if land and #Map.ActorsInCircle(Map.CenterOfCell(cell), WDist.New(512)) == 0 then
				cells[#cells + 1] = cell
				if #cells == wanted then
					return cells
				end
			end
		end
	end

	return cells
end

-- Jumps up to four Allied tanks from home into the battle zone. Skipped when nothing can land.
Chronoshift = function()
	local tanks = Utils.Where(Allies.GetActorsByTypes({ "mtnk", "sref", "mgtk" }), function(a) return not a.IsDead end)
	local cells = FreeLandCells(StrikeTarget(), math.min(4, #tanks))
	local jumps = { }
	for i, cell in ipairs(cells) do
		jumps[tanks[i]] = cell
	end

	if #cells > 0 then
		Chronosphere.Chronoshift(jumps)
	end
end

IronCurtain = function()
	local tanks = UnitsInBattleZone(function(a) return a.Owner == Soviets and Utils.Any(SovietTanks, function(t) return t == a.Type end) end)
	for i = 1, math.min(4, #tanks) do
		tanks[i].GrantCondition("invulnerability", 500)
	end
end

Nuke = function()
	NukeSilo.ActivateNukePower(StrikeTarget())
end

WeatherStorm = function()
	WeatherDevice.ActivateWeatherControl(StrikeTarget())
end

-- Raids: a small party charges up a base's ramp so its defences fire while the camera is there.
AlliedRaid = { "mtnk", "mtnk", "sref", "sref" }
SovietRaid = { "htnk", "htnk", "apoc", "ttnk" }

Raid = function(player, types, foot, base)
	local raiders = Reinforcements.Reinforce(player, types, { foot }, 10)
	Utils.Do(raiders, function(unit)
		unit.AttackMove(base.Location)
		Trigger.OnIdle(unit, function(u) u.AttackMove(base.Location) end)
	end)
end

RaidSovietBase = function()
	Trigger.AfterDelay(DateTime.Seconds(10), function() Raid(Allies, AlliedRaid, SovietRampFoot.Location, SovietConyard) end)
end

RaidAlliedBase = function()
	Trigger.AfterDelay(DateTime.Seconds(18), function() Raid(Soviets, SovietRaid, AlliedRampFoot.Location, AlliedConyard) end)
end

-- Alternates a superweapon strike with a Chronosphere jump or Iron Curtain, one per pass over the strip.
-- With the camera loop below, a superweapon lands about every two minutes.
EventCycle = { Chronoshift, Nuke, IronCurtain, WeatherStorm }
NextEvent = 1

-- The camera loops over these stops; at a stop with an event it pauses while the event plays out.
CameraSpeed = 72
CameraRoute = { }
CameraLeg = 1
CameraHold = 0

BuildCameraRoute = function()
	CameraRoute = {
		{ position = AlliedConyard.CenterPosition, hold = DateTime.Seconds(10) },
		{ position = StripCenter.CenterPosition, hold = DateTime.Seconds(12), event = true },
		{ position = SeaBattle.CenterPosition, hold = 0, arrive = RaidSovietBase },
		{ position = SovietConyard.CenterPosition, hold = DateTime.Seconds(10) },
		{ position = StripCenter.CenterPosition, hold = DateTime.Seconds(14), event = true, arrive = RaidAlliedBase },
	}
	CameraAt = AlliedConyard.CenterPosition
end

MoveCamera = function()
	if CameraHold > 0 then
		CameraHold = CameraHold - 1
		return
	end

	local stop = CameraRoute[CameraLeg]
	local dx = stop.position.X - CameraAt.X
	local dy = stop.position.Y - CameraAt.Y
	local distance = math.sqrt(dx * dx + dy * dy)

	-- Slow down over the last few cells so stops do not jerk.
	local step = math.min(CameraSpeed, distance / 12 + 8)
	if distance <= step then
		CameraAt = stop.position
		CameraHold = stop.hold
		if stop.arrive then
			stop.arrive()
		end

		if stop.event then
			EventCycle[NextEvent]()
			NextEvent = NextEvent % #EventCycle + 1
		end

		CameraLeg = CameraLeg % #CameraRoute + 1
	else
		CameraAt = WPos.New(math.floor(CameraAt.X + dx * step / distance), math.floor(CameraAt.Y + dy * step / distance), 0)
	end

	Camera.Position = CameraAt
end

WorldLoaded = function()
	Allies = Player.GetPlayer("Allies")
	Soviets = Player.GetPlayer("Soviets")

	AlliedFactoryExit = AlliedFactory.Location + CVec.New(5, 1)
	AlliedBarracksExit = AlliedBarracks.Location + CVec.New(1, 2)
	SovietFactoryExit = SovietFactory.Location + CVec.New(5, 1)
	SovietBarracksExit = SovietBarracks.Location + CVec.New(1, 2)
	AlliedYardExit = AlliedYard.Location + CVec.New(2, 5)
	SovietYardExit = SovietYard.Location + CVec.New(2, 5)
	AlliedAirEdge = Map.ClosestEdgeCell(AlliedAirfield.Location)
	SovietAirEdge = Map.ClosestEdgeCell(SovietConyard.Location)

	BuildCameraRoute()

	ChargeTeslaCoils()
	AlliedWaves()
	Trigger.AfterDelay(DateTime.Seconds(5), SovietWaves)
	TopUpFleets()
	Trigger.AfterDelay(DateTime.Seconds(20), AlliedAir)
	Trigger.AfterDelay(DateTime.Seconds(40), KirovRun)
end

Tick = function()
	MoveCamera()
end
