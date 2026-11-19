--[[
   Copyright (c) The OpenRA Developers and Contributors
   This file is part of OpenRA, which is free software. It is made
   available to you under the terms of the GNU General Public License
   as published by the Free Software Foundation, either version 3 of
   the License, or (at your option) any later version. For more
   information, see COPYING.
]]

WorldLoaded = function()
	-- TEMPORARY terrain dump for planning the base layout (Task 3 removes it).
	for x = -20, 220 do
		for y = -130, 130 do
			local ok, t = pcall(function() return Map.TerrainType(CPos.New(x, y)) end)
			if ok and t ~= nil then
				print("TERRAIN " .. x .. "," .. y .. "," .. t)
			end
		end
	end
end

Tick = function()
end
