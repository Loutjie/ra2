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

AlliedWave = { "mtnk", "mtnk", "sref", "mgtk", "fv", "e1", "e1", "e1" }
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

AttackStrip = function(units)
	Utils.Do(units, function(unit)
		if not unit.IsDead then
			unit.AttackMove(StripCenter.Location)
			Trigger.OnIdle(unit, function(u) u.AttackMove(StripCenter.Location) end)
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

-- Flies in from the map edge, attacks, then leaves the way it came.
Sortie = function(player, type, edge, target)
	local aircraft = Reinforcements.Reinforce(player, { type }, { edge, target })[1]
	aircraft.AttackMove(target)
	aircraft.Move(edge)
	aircraft.Destroy()
end

AlliedAir = function()
	Sortie(Allies, Utils.Random({ "orca", "beag" }), AlliedAirEdge, StripCenter.Location)
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

	Camera.Position = StripCenter.CenterPosition

	ChargeTeslaCoils()
	AlliedWaves()
	Trigger.AfterDelay(DateTime.Seconds(5), SovietWaves)
	TopUpFleets()
	Trigger.AfterDelay(DateTime.Seconds(20), AlliedAir)
	Trigger.AfterDelay(DateTime.Seconds(40), KirovRun)
end

Tick = function()
end
