using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.Entities;
using System;
using System.Collections.Generic;
using System.Timers;


namespace DynamicMazeMod
{
    public class DynamicMazeModSystem : ModSystem
    {
        ICoreServerAPI api;
        private MazeConfig Config;
        MazeGenerator generator;
        BlockPos mazeOrigin;
        int mazeWidth, mazeHeight;

        Timer rewardTimer;
        Timer mobSpawnTimer;

        Dictionary<string, long> playerStartTimes = new Dictionary<string, long>();
        Dictionary<string, float> playerBestTimes = new Dictionary<string, float>();

        List<Entity> activeMobs = new List<Entity>();
        bool mobActive = false;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            Config = api.LoadModConfig<MazeConfig>("config.json");

            if (Config.allowCommandActivation)
            {
                api.ChatCommands.Create("laberinto")
                    .WithDescription("Inicia el laberinto dinámico en tu posición.")
                    .RequiresPrivilege("gamemode")
                    .HandleWith(OnLaberintoCommand);

                api.ChatCommands.Create("laberintotamaño")
                    .WithDescription("Cambia el tamaño del laberinto. Uso: /laberintotamaño pequeño|mediano|grande")
                    .RequiresPrivilege("gamemode")
                    .HandleWith(OnChangeSizeCommand);

                api.ChatCommands.Create("laberintocoferandom")
                    .WithDescription("Activa/desactiva cofres aleatorios automáticos. Uso: /laberintocoferandom on|off")
                    .RequiresPrivilege("gamemode")
                    .HandleWith(OnToggleRandomChestsCommand);

                api.ChatCommands.Create("laberintomobs")
                    .WithDescription("Activa/desactiva aparición de mobs. Uso: /laberintomobs on|off")
                    .RequiresPrivilege("gamemode")
                    .HandleWith(OnToggleMobsCommand);

                api.ChatCommands.Create("laberintoranking")
                    .WithDescription("Muestra el ranking de mejores tiempos en el laberinto.")
                    .RequiresPrivilege("gamemode")
                    .HandleWith(OnShowRankingCommand);
            }
            else
            {
                api.Server.Logger.Notification("[dynamicmazemod] El comando '/laberinto' está deshabilitado por configuración.");
            }

            // Timers para eventos automáticos
            rewardTimer = new Timer(Config.eventIntervalSeconds * 1000);
            rewardTimer.Elapsed += (sender, e) => SpawnRandomChest();
            rewardTimer.AutoReset = true;

            mobSpawnTimer = new Timer(Config.eventIntervalSeconds * 1000);
            mobSpawnTimer.Elapsed += (sender, e) => SpawnMobInMaze();
            mobSpawnTimer.AutoReset = true;
        }

        private void LoadConfig()
        {
            string filename = "config.json";
            Config = api.LoadModConfig<MazeConfig>(filename);
            if (Config == null)
            {
                api.Server.Logger.Warning("[dynamicmazemod] No se encontró config.json, se crea uno por defecto.");
                Config = new MazeConfig();
                api.StoreModConfig(Config, filename);
            }
            else
            {
                api.Server.Logger.Notification("[dynamicmazemod] Configuración cargada correctamente.");
            }
        }

        private TextCommandResult OnLaberintoCommand(TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            BlockPos playerPos = player.Entity.Pos.AsBlockPos;

            if (!Config.allowCommandActivation)
            {
                return TextCommandResult.Error("Este comando está desactivado por configuración.");
            }

            BlockFacing facing = GetFacingFromYaw(player.Entity.Pos.Yaw);
            BlockPos pedestalPos = playerPos.AddCopy((int)facing.Normalf.X, 0, (int)facing.Normalf.Z);

            Block pedestalBlock = api.World.GetBlock(new AssetLocation(Config.pedestalBlock));
            if (pedestalBlock == null || pedestalBlock.BlockId <= 0)
            {
                (player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup,
                    $"Error: Bloque '{Config.pedestalBlock}' no encontrado o inválido.",
                    EnumChatType.CommandError);
                return TextCommandResult.Error("Bloque pedestal inválido.");
            }

            api.World.BlockAccessor.BreakBlock(pedestalPos, null);
            api.World.BlockAccessor.SetBlock(pedestalBlock.BlockId, pedestalPos);
            api.World.BlockAccessor.MarkBlockDirty(pedestalPos);

            mazeWidth = Config.defaultSize switch
            {
                "pequeño" => 25,
                "mediano" => 50,
                "grande" => 150,
                _ => 150
            };

            mazeHeight = Config.mazeHeight;

            mazeOrigin = pedestalPos.AddCopy((int)(facing.Normalf.X * 2), 0, (int)(facing.Normalf.Z * 2));

            generator = new MazeGenerator(api,
                mazeWidth,
                mazeWidth,
                mazeHeight,
                Config.wallBlockTypes[0],
                Config.floorBlock,
                Config.trapBlock,
                Config.rewardBlock,
                Config.trapChance,
                Config.rewardChance);

            generator.Generate(mazeOrigin);

            if (Config.enableRanking)
            {
                string playerUid = player.PlayerUID;
                playerStartTimes[playerUid] = api.World.ElapsedMilliseconds;
            }

            rewardTimer.Start();
            mobSpawnTimer.Start();

            (player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup,
                Config.startMessage + " Apareció un frente a ti.",
                EnumChatType.CommandSuccess);

            return TextCommandResult.Success("Laberinto iniciado.");
        }

        private TextCommandResult OnChangeSizeCommand(TextCommandCallingArgs args)
        {
            string rawArgs = args.RawArgs.ToString();
            string[] cmdArgs = rawArgs?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cmdArgs == null || cmdArgs.Length != 1)
            {
                return TextCommandResult.Error("Uso: /laberintotamaño pequeño|mediano|grande");
            }

            string size = cmdArgs[0].ToLowerInvariant();

            if (size != "pequeño" && size != "mediano" && size != "grande")
            {
                return TextCommandResult.Error("Tamaño inválido. Use: pequeño, mediano o grande.");
            }

            Config.defaultSize = size;
            api.StoreModConfig(Config, "config.json");

            return TextCommandResult.Success($"Tamaño del laberinto cambiado a '{size}'. Usa /laberinto para generar.");
        }


        private bool randomChestsActive = false;
        private TextCommandResult OnToggleRandomChestsCommand(TextCommandCallingArgs args)
        {
            string rawArgs = args.RawArgs.ToString();
            string[] cmdArgs = rawArgs?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cmdArgs == null || cmdArgs.Length != 1)
            {
                return TextCommandResult.Error("Uso: /laberintocoferandom on|off");
            }

            string val = cmdArgs[0].ToLowerInvariant();
            if (val == "on")
            {
                randomChestsActive = true;
                rewardTimer.Start();
                return TextCommandResult.Success("Cofres aleatorios activados.");
            }
            else if (val == "off")
            {
                randomChestsActive = false;
                rewardTimer.Stop();
                return TextCommandResult.Success("Cofres aleatorios desactivados.");
            }
            else
            {
                return TextCommandResult.Error("Valor inválido. Use 'on' o 'off'.");
            }
        }


        private bool mobsActive = false;
        private TextCommandResult OnToggleMobsCommand(TextCommandCallingArgs args)
        {
            string rawArgs = args.RawArgs.ToString();
            string[] cmdArgs = rawArgs?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cmdArgs == null || cmdArgs.Length != 1)
            {
                return TextCommandResult.Error("Uso: /laberintomobs on|off");
            }

            string val = cmdArgs[0].ToLowerInvariant();
            if (val == "on")
            {
                mobsActive = true;
                mobSpawnTimer.Start();
                return TextCommandResult.Success("Mobs activados.");
            }
            else if (val == "off")
            {
                mobsActive = false;
                mobSpawnTimer.Stop();
                return TextCommandResult.Success("Mobs desactivados.");
            }
            else
            {
                return TextCommandResult.Error("Valor inválido. Use 'on' o 'off'.");
            }
        }



        private TextCommandResult OnShowRankingCommand(TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            string playerUid = player.PlayerUID;

            if (!Config.enableRanking)
            {
                return TextCommandResult.Error("El ranking está deshabilitado por configuración.");
            }

            if (playerBestTimes.Count == 0)
            {
                (player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, "No hay registros de tiempos aún.", EnumChatType.CommandSuccess);
                return TextCommandResult.Success("Ranking vacío.");
            }

            var rankingText = "Ranking de mejores tiempos:\n";
            int rank = 1;

            foreach (var kvp in playerBestTimes)
            {
                rankingText += $"{rank}. {kvp.Key} - {kvp.Value:F2} segundos\n";
                rank++;
            }

            (player as IServerPlayer)?.SendMessage(GlobalConstants.GeneralChatGroup, rankingText.Trim(), EnumChatType.CommandSuccess);
            return TextCommandResult.Success("Ranking mostrado.");
        }

        private void SpawnRandomChest()
        {
            if (!randomChestsActive) return;
            if (mazeOrigin == null) return;

            int x = api.World.Rand.Next(mazeOrigin.X, mazeOrigin.X + mazeWidth);
            int z = api.World.Rand.Next(mazeOrigin.Z, mazeOrigin.Z + mazeWidth);
            int y = mazeOrigin.Y;

            BlockPos chestPos = new BlockPos(x, y, z);
            Block block = api.World.BlockAccessor.GetBlock(chestPos);

            // Verifica si se puede reemplazar el bloque actual por el cofre
            if (block.IsReplacableBy(api.World.GetBlock(new AssetLocation("game:air"))))
            {
                // Coloca el cofre
                var chestBlock = api.World.GetBlock(new AssetLocation(Config.chestBlock));
                api.World.BlockAccessor.SetBlock(chestBlock.BlockId, chestPos);

                // Obtener la entidad del bloque cofre para llenarlo
                if (api.World.BlockAccessor.GetBlockEntity(chestPos) is BlockEntityContainer be && be.Inventory != null)
                {
                    int lootCount = api.World.Rand.Next(Config.minLootCount, Config.maxLootCount + 1);

                    for (int i = 0; i < lootCount; i++)
                    {
                        string lootItemCode = Config.possibleLoot[api.World.Rand.Next(Config.possibleLoot.Count)];
                        ItemStack stack = new ItemStack(api.World.GetItem(new AssetLocation(lootItemCode)), 1);

                        // Agrega el item a un slot vacío
                        bool added = false;
                        for (int slot = 0; slot < be.Inventory.Count; slot++)
                        {
                            if (be.Inventory[slot].Empty)
                            {
                                be.Inventory[slot].Itemstack = stack;
                                added = true;
                                break;
                            }
                        }
                        if (!added)
                        {
                            // Inventario lleno, termina aquí
                            break;
                        }
                    }
                    be.MarkDirty();
                }
            }
        }


        private void SpawnMobInMaze()
        {
            if (!mobsActive) return;
            if (mazeOrigin == null) return;
            if (mobActive) return; // Solo un mob a la vez

            int x = api.World.Rand.Next(mazeOrigin.X, mazeOrigin.X + mazeWidth);
            int z = api.World.Rand.Next(mazeOrigin.Z, mazeOrigin.Z + mazeWidth);
            int y = mazeOrigin.Y + 1;

            Entity mob = api.World.ClassRegistry.CreateEntity("game:chicken");
            mob.Pos = new EntityPos()
            {
                X = x + 0.5,
                Y = y,
                Z = z + 0.5
            };

            api.World.SpawnEntity(mob);
            activeMobs.Add(mob);
            mobActive = true;
        }

        private BlockFacing GetFacingFromYaw(float yaw)
        {
            int dir = (int)MathF.Floor((yaw * 4f / 360f) + 0.5f) & 3;
            return dir switch
            {
                0 => BlockFacing.NORTH,
                1 => BlockFacing.EAST,
                2 => BlockFacing.SOUTH,
                3 => BlockFacing.WEST,
                _ => BlockFacing.NORTH
            };
        }
    }

    public class MazeConfig
    {

        public List<string> possibleLoot { get; set; } = new List<string>()
    {
        "game:bread",
        "game:apple",
        "game:gear-rusty",
        "game:torch",
        "game:iron-ingot"
    };

        public int minLootCount { get; set; } = 1;
        public int maxLootCount { get; set; } = 4;
        public string defaultSize { get; set; } = "grande";
        public int mazeHeight { get; set; } = 10;

        public string pedestalBlock { get; set; } = "game:stone";
        public string[] wallBlockTypes { get; set; } = new string[] { "game:stone" };
        public string floorBlock { get; set; } = "game:dirt";
        public string trapBlock { get; set; } = "game:lava";
        public string rewardBlock { get; set; } = "game:goldblock";
        public string chestBlock { get; set; } = "game:chest";

        public float trapChance { get; set; } = 0.1f;
        public float rewardChance { get; set; } = 0.05f;

        public bool allowCommandActivation { get; set; } = true;
        public bool enableRanking { get; set; } = true;

        public int eventIntervalSeconds { get; set; } = 60;

        public string startMessage { get; set; } = "¡El laberinto dinámico ha aparecido!";
    }
}
