using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System;
using Newtonsoft.Json;
using System.IO;
using Vintagestory.API.MathTools;

[assembly: ModInfo("dynamicmazemod", "dynamicmazemod", Version = "1.0.0", Authors = new[] { "MelliRunH" })]

namespace DynamicMazeMod
{
    public class DynamicMazeModSystem : ModSystem
    {
        ICoreServerAPI api;
        public static MazeConfig Config;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            // Cargar el archivo de configuración
            LoadConfig();

            // Registrar comando /laberinto
            api.ChatCommands.Create("laberinto")
                .WithDescription("Inicia el laberinto dinámico en tu posición.")
                .RequiresPrivilege("gamemode")
                .HandleWith(OnLaberintoCommand);
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(api.ModLoader.GetModFolder(this), "config.json");


            if (File.Exists(configPath))
            {
                try
                {
                    string configContent = File.ReadAllText(configPath);
                    Config = JsonConvert.DeserializeObject<MazeConfig>(configContent);
                }
                catch (Exception ex)
                {
                    api.Server.Logger.Error("Error cargando el archivo config.json: " + ex.Message);
                    Config = new MazeConfig(); // Configuración por defecto en caso de error
                }
            }
            else
            {
                api.Server.Logger.Warning("No se encontró el archivo config.json, usando configuración por defecto.");
                Config = new MazeConfig(); // Configuración por defecto si el archivo no existe
            }
        }

        private TextCommandResult OnLaberintoCommand(TextCommandCallingArgs args)
        {
            IPlayer player = args.Caller.Player;
            BlockPos playerPos = player.Entity.Pos.AsBlockPos;

            // Posición para colocar el pedestal frente al jugador
            BlockFacing facing = BlockFacing.FromAngle(player.Entity.Pos.Yaw);

            BlockPos pedestalPos = playerPos.AddCopy(facing.Normalf.X, 0, facing.Normalf.Z);

            Block pedestalBlock = api.World.GetBlock(new AssetLocation(Config.pedestalBlock));
            if (pedestalBlock != null)
            {
                api.World.BlockAccessor.SetBlock(pedestalBlock.BlockId, pedestalPos);
                api.World.BlockAccessor.MarkBlockDirty(pedestalPos);
            }

            // Usar el método correcto para enviar un mensaje
            api.World.SendMessageToClient(player, "Has iniciado el laberinto dinámico. Apareció un pedestal frente a ti.");


            return TextCommandResult.Success("Laberinto iniciado.");
        }
    }

    public class MazeConfig
    {
        public string defaultSize { get; set; } = "mediano";
        public bool allowCommandActivation { get; set; } = true;
        public bool usePedestal { get; set; } = true;
        public bool spawnAtPlayer { get; set; } = true;
        public bool announceCoordinates { get; set; } = true;
        public string mazeDifficulty { get; set; } = "medio";
        public double trapChance { get; set; } = 0.25;
        public double rewardChance { get; set; } = 0.15;
        public int maxPlayers { get; set; } = 10;
        public int mazeDuration { get; set; } = 600;
        public bool enableRanking { get; set; } = true;
        public string pedestalBlock { get; set; } = "game:stonepath";
    }
}
