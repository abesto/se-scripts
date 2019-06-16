using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        public class MyBlockCache
        {
            private TimeSpan Ttl;
            public TimeSpan Age = new TimeSpan();

            private IMyGridTerminalSystem GridTerminalSystem;
            private IMyGridProgramRuntimeInfo Runtime;

            public List<IMyTerminalBlock> Blocks { get; } = new List<IMyTerminalBlock>();
            public List<IMyPowerProducer> PowerProducers { get; } = new List<IMyPowerProducer>();
            public List<IMyBatteryBlock> Batteries { get; } = new List<IMyBatteryBlock>();
            public List<IMyInventory> Inventories { get; } = new List<IMyInventory>();
            public List<IMyProductionBlock> ProductionBlocks { get; } = new List<IMyProductionBlock>();
            private Dictionary<String, IMyTerminalBlock> BlocksByName = new Dictionary<string, IMyTerminalBlock>();

            public MyBlockCache(IMyGridTerminalSystem gridTerminalSystem, IMyGridProgramRuntimeInfo runtime, int ttlSeconds)
            {
                Ttl = new TimeSpan(0, 0, ttlSeconds);
                GridTerminalSystem = gridTerminalSystem;
                Runtime = runtime;
                Update(true);
            }

            public bool CacheUpdatedThisTick => Age.Ticks == 0;

            public void Update(bool bustCache = false)
            {
                Age += Runtime.TimeSinceLastRun;

                if (Age < Ttl && !bustCache)
                {
                    Blocks.RemoveAll(x => x == null);
                    PowerProducers.RemoveAll(x => x == null);
                    Batteries.RemoveAll(x => x == null);
                    Inventories.RemoveAll(x => x == null);
                    ProductionBlocks.RemoveAll(x => x == null);

                    var keysToRemove = BlocksByName.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList();
                    foreach (var key in keysToRemove)
                    {
                        BlocksByName.Remove(key);
                    }

                    return;
                }

                Batteries.Clear();
                PowerProducers.Clear();
                Inventories.Clear();
                ProductionBlocks.Clear();
                BlocksByName.Clear();

                GridTerminalSystem.GetBlocks(Blocks);

                foreach (var block in Blocks)
                {
                    if (block is IMyBatteryBlock battery)
                    {
                        Batteries.Add(battery);
                    }
                    if (block is IMyPowerProducer producer)
                    {
                        PowerProducers.Add(producer);
                    }
                    if (block is IMyProductionBlock production)
                    {
                        ProductionBlocks.Add(production);
                    }

                    for (var inventoryIndex = 0; inventoryIndex < block.InventoryCount; inventoryIndex++)
                    {
                        Inventories.Add(block.GetInventory(inventoryIndex));
                    }
                }

                Age = new TimeSpan();
            }

            public IMyTerminalBlock GetBlockWithName(String name)
            {
                if (!BlocksByName.ContainsKey(name))
                {
                    BlocksByName[name] = GridTerminalSystem.GetBlockWithName(name);
                }
                return BlocksByName[name];
            }
        }

        public class MyDataCache
        {
            private Dictionary<String, String> Data = new Dictionary<string, string>();
            public int Hits;
            public int Misses;

            public void Clear()
            {
                Data.Clear();
                Hits = 0;
                Misses = 0;
            }

            public String GetOrGenerate(String key, Func<String> generate)
            {
                if (!Data.ContainsKey(key))
                {
                    Data[key] = generate();
                    Misses += 1;
                }
                else
                {
                    Hits += 1;
                }
                return Data[key];
            }
        }

        public class Dashboard
        {
            private MyIniKey IniKeyBlocks = new MyIniKey(INI_SECTION, "blocks");
            private MyIniKey IniKeyPower = new MyIniKey(INI_SECTION, "power");
            private MyIniKey IniKeyCargo = new MyIniKey(INI_SECTION, "cargo");
            private MyIniKey IniKeyProduction = new MyIniKey(INI_SECTION, "production");
            private MyIniKey IniKeyWidth = new MyIniKey(INI_SECTION, "width");
            private MyIniKey IniKeyStatus = new MyIniKey(INI_SECTION, "status");

            private IMyTextPanel Output;
            private bool ShouldDisplayPower;
            private bool ShouldDisplayCargo;
            private bool ShouldDisplayProduction;
            private bool ShouldDisplayStatus;
            private ushort Width;

            private List<IMyTerminalBlock> Blocks { get; } = new List<IMyTerminalBlock>();
            private MyBlockCache BlockCache;
            private MyDataCache DataCache;

            private StringBuilder Str = new StringBuilder();
            private StringBuilder DiscoveryStatusBuilder = new StringBuilder();

            public Dashboard(MyBlockCache blockCache, MyDataCache dataCache, IMyTextPanel output)
            {
                BlockCache = blockCache;
                DataCache = dataCache;
                Output = output;
            }

            public void LoadConfig(MyIni ini)
            {
                DiscoveryStatusBuilder.Clear();

                bool power;
                if (ini.Get(IniKeyPower).TryGetBoolean(out power))
                {
                    ShouldDisplayPower = power;
                }
                else
                {
                    ShouldDisplayPower = false;
                }

                bool cargo;
                if (ini.Get(IniKeyCargo).TryGetBoolean(out cargo))
                {
                    ShouldDisplayCargo = cargo;
                }
                else
                {
                    ShouldDisplayCargo = false;
                }

                bool production;
                if (ini.Get(IniKeyProduction).TryGetBoolean(out production))
                {
                    ShouldDisplayProduction = production;
                }
                else
                {
                    ShouldDisplayProduction = false;
                }

                bool status;
                if (ini.Get(IniKeyStatus).TryGetBoolean(out status))
                {
                    ShouldDisplayStatus = status;
                }
                else
                {
                    ShouldDisplayStatus = false;
                }

                ushort width;
                if (ini.Get(IniKeyWidth).TryGetUInt16(out width))
                {
                    Width = width;
                }
                else
                {
                    Width = 40;
                }

                String blocks;
                Blocks.Clear();
                if (ini.Get(IniKeyBlocks).TryGetString(out blocks))
                {
                    foreach (var blockName in blocks.Split(new char[0], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmedBlockName = blockName.Trim();
                        var block = BlockCache.GetBlockWithName(trimmedBlockName);
                        if (block != null)
                        {
                            Blocks.Add(block);
                        }
                        else
                        {
                            DiscoveryStatusBuilder.Append($"{Output.CustomName} failed to find {trimmedBlockName}\n");
                        }
                    }
                }
            }

            private void RecordUsingCache(String cacheKey, Func<String> generate)
            {
                Str.Append(DataCache.GetOrGenerate(cacheKey, generate));
            }

            private void DisplayPiston(IMyExtendedPistonBase piston)
            {
                RecordUsingCache(piston.EntityId.ToString(), () =>
                    $"= Piston[{piston.CustomName}] =\n" +
                    $"Status: {piston.Status}\n" +
                    $"Position: {piston.CurrentPosition}\n" +
                    $"Velocity: {piston.Velocity}\n" +
                    $"Limit: {piston.MaxLimit}\n\n"
                );
            }

            private void DisplayAdvancedRotor(IMyMotorAdvancedStator rotor)
            {
                RecordUsingCache(rotor.EntityId.ToString(), () =>
                    $"= AdvancedRotor[{rotor.CustomName}] =\n" +
                    $"Angle (deg): {Math.Round(rotor.Angle * 180 / Math.PI).ToString()}\n" +
                    $"Target RPM: {Math.Round(rotor.TargetVelocityRPM, 1)}\n\n"
                );
            }

            private void DisplayDrill(IMyShipDrill drill)
            {
                RecordUsingCache(drill.EntityId.ToString(), () =>
                    $"= Drill[{drill.CustomName}] =\n" +
                    $"Status: {(drill.Enabled ? "On" : "Off")}\n" +
                    $"Inventory used (m^3): {drill.GetInventory().CurrentVolume}\n\n"
                );
            }

            private void DisplayConnector(IMyShipConnector connector)
            {
                RecordUsingCache(connector.EntityId.ToString(), () =>
                {
                    var str = new StringBuilder(
                        $"= Connector[{connector.CustomName}] =\n" +
                        $"Inventory used (m^3): {connector.GetInventory().CurrentVolume}\n" +
                        $"Status: {connector.Status}\n"
                    );

                    if (connector.Status == MyShipConnectorStatus.Connected)
                    {
                        str.Append($"Docked: {connector.OtherConnector.CubeGrid.CustomName}\n");
                    }

                    str.Append("\n");

                    return str.ToString();
                });
            }

            private void DisplayComponents()
            {
                foreach (var component in Blocks)
                {
                    if (component is IMyExtendedPistonBase piston)
                    {
                        DisplayPiston(piston);
                    }
                    else if (component is IMyMotorAdvancedStator rotor)
                    {
                        DisplayAdvancedRotor(rotor);
                    }
                    else if (component is IMyShipDrill drill)
                    {
                        DisplayDrill(drill);
                    }
                    else if (component is IMyShipConnector connector)
                    {
                        DisplayConnector(connector);
                    }
                }
            }

            private void DisplayPower()
            {
                if (!ShouldDisplayPower)
                {
                    return;
                }

                RecordUsingCache("power", () =>
                {
                    StringBuilder str = new StringBuilder();
                    float sumMaxOutput = 0;
                    float sumCurrentOutput = 0;
                    foreach (var producer in BlockCache.PowerProducers)
                    {
                        if (producer is IMyBatteryBlock battery && battery.ChargeMode == ChargeMode.Recharge)
                        {
                            continue;
                        }
                        sumMaxOutput += producer.MaxOutput;
                        sumCurrentOutput += producer.CurrentOutput;
                    }
                    str.Append($"Power consumption: {Math.Round(sumCurrentOutput, 2)} / {Math.Round(sumMaxOutput, 2)} MW\n");

                    float sumCurrentStoredPower = 0f;
                    float sumCurrentInput = 0f;
                    float sumMaxStoredPower = 0f;
                    sumCurrentOutput = 0f;
                    int batteriesCharging = 0;
                    int batteriesDischarging = 0;

                    foreach (var battery in BlockCache.Batteries)
                    {
                        sumCurrentStoredPower += battery.CurrentStoredPower;
                        sumCurrentInput += battery.CurrentInput;
                        sumCurrentOutput += battery.CurrentOutput;
                        sumMaxStoredPower += battery.MaxStoredPower;
                        if (battery.IsCharging)
                        {
                            batteriesCharging += 1;
                        }
                        else if (battery.CurrentOutput > 0)
                        {
                            batteriesDischarging += 1;
                        }
                    }
                    str.Append($"Stored: {Math.Round(sumCurrentStoredPower, 2)} / {Math.Round(sumMaxStoredPower, 2)} MW/h ({BlockCache.Batteries.Count} batteries)\n");
                    str.Append($"{batteriesCharging} charging, {batteriesDischarging} discharging batteries\n\n");

                    return str.ToString();
                });
            }

            private StringBuilder DisplayCounts(Dictionary<String, float> counts)
            {
                StringBuilder str = new StringBuilder();
                // Once MeasureStringInPixels is fixed for dedicated servers we can switch to text wrapping based on that
                // float width = Output.SurfaceSize.X * (1.0f - Output.TextPadding);
                // float itemLength = Output.MeasureStringInPixels(itemStr, Output.Font, Output.FontSize).X;
                int lineLength = 0;
                foreach (var item in counts)
                {
                    var itemStr = new StringBuilder($"{item.Key}:{Math.Round(item.Value, 1)}  ");
                    int itemLength = itemStr.Length;
                    if (lineLength + itemLength > Width)
                    {
                        str.Append("\n");
                        lineLength = 0;
                    }
                    str.Append(itemStr);
                    lineLength += itemLength;
                }
                str.Append("\n");
                return str;
            }

            private void DisplayCargo()
            {
                if (!ShouldDisplayCargo)
                {
                    return;
                }

                RecordUsingCache("cargo", () =>
                {
                    Dictionary<String, float> counts = new Dictionary<String, float>();
                    foreach (var inventory in BlockCache.Inventories)
                    {
                        List<MyInventoryItem> items = new List<MyInventoryItem>();
                        inventory.GetItems(items);
                        foreach (var item in items)
                        {
                            String name = item.Type.SubtypeId;
                            float amount = (float)item.Amount;
                            if (!counts.ContainsKey(name))
                            {
                                counts[name] = amount;
                            }
                            else
                            {
                                counts[name] += amount;
                            }
                        }
                    }
                    return $"= Cargo =\n{DisplayCounts(counts)}\n";
                });
            }

            public void DisplayProduction()
            {
                if (!ShouldDisplayProduction)
                {
                    return;
                }

                RecordUsingCache("production", () =>
                {
                    Dictionary<String, float> counts = new Dictionary<String, float>();
                    List<MyProductionItem> queue = new List<MyProductionItem>();
                    foreach (var producer in BlockCache.ProductionBlocks)
                    {
                        producer.GetQueue(queue);
                        foreach (var item in queue)
                        {
                            String name = item.BlueprintId.SubtypeName;
                            float amount = (float)item.Amount;
                            if (!counts.ContainsKey(name))
                            {
                                counts[name] = amount;
                            }
                            else
                            {
                                counts[name] += amount;
                            }
                        }
                    }

                    return $"= Production =\n{DisplayCounts(counts)}\n";
                });
            }

            private void DisplayStatus(String status)
            {
                if (!ShouldDisplayStatus)
                {
                    return;
                }
                Str.Append("= Dashboard System =\n");
                Str.Append(status);
            }

            public void Update(String status)
            {
                Str.Clear();
                Blocks.RemoveAll(item => item == null);

                DisplayPower();
                DisplayCargo();
                DisplayProduction();
                DisplayComponents();
                DisplayStatus(status);

                Output.ContentType = ContentType.TEXT_AND_IMAGE;
                Output.WriteText(Str);
            }

            public String DiscoveryStatus => DiscoveryStatusBuilder.ToString();
        }

        private const String INI_SECTION = "dashboard";

        private StringBuilder DiscoveryStatusBuilder = new StringBuilder();
        private Dictionary<long, Dashboard> Dashboards = new Dictionary<long, Dashboard>();
        private MyBlockCache BlockCache;
        private IMyTextSurface DebugSurface;
        private int LastInstructionCount;
        private MyDataCache DataCache = new MyDataCache();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once | UpdateFrequency.Update100;
            BlockCache = new MyBlockCache(GridTerminalSystem, Runtime, 10);
            DebugSurface = Me.GetSurface(0);
        }

        private void DiscoverDashboards()
        {
            var prevDashboards = Dashboards;
            Dashboards = new Dictionary<long, Dashboard>();

            var ini = new MyIni();
            List<IMyTextPanel> outputs = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(outputs, output => MyIni.HasSection(output.CustomData, INI_SECTION));
            foreach (var output in outputs)
            {
                if (!ini.TryParse(output.CustomData, INI_SECTION))
                {
                    DiscoveryStatusBuilder.Append($"Failed to parse INI of {output.CustomName}");
                    continue;
                }

                var dashboard = prevDashboards.ContainsKey(output.EntityId) ? prevDashboards[output.EntityId] : new Dashboard(BlockCache, DataCache, output);
                dashboard.LoadConfig(ini);
                Dashboards.Add(output.EntityId, dashboard);
            }
        }

        private void Debug(String message)
        {
            Echo(message);
            DebugSurface.WriteText(message, true);
        }

        private String GenerateStatus()
        {
            return 
                $"Dashboards: {Dashboards.Count}\n" +
                $"Grid: {Me.CubeGrid.DisplayName}\n" +
                $"Blocks: {BlockCache.Blocks.Count}\n" +
                $"Block cache age: {Math.Round(BlockCache.Age.TotalSeconds, 1)}s\n" +
                $"Data cache: {DataCache.Hits} hits / {DataCache.Misses} misses\n" +
                $"Last instruction count: {LastInstructionCount}\n" +
                DiscoveryStatusBuilder.ToString() + 
                String.Join("", from dashboard in Dashboards.Values select dashboard.DiscoveryStatus);
        }


        public void Main(string argument, UpdateType updateSource)
        {
            String status = GenerateStatus();
            Debug(status);

            BlockCache.Update();
            DataCache.Clear();
            DebugSurface.WriteText("");

            if (BlockCache.CacheUpdatedThisTick)
            {
                DiscoveryStatusBuilder.Clear();
                DiscoverDashboards();
            }
            foreach (var dashboard in Dashboards.Values)
            {
                dashboard.Update(status);
            }
            LastInstructionCount = Runtime.CurrentInstructionCount;
        }
    }
}
