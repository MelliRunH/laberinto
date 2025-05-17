using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using System;


namespace DynamicMazeMod
{
    public class MazeGenerator
    {
        private ICoreServerAPI api;
        private int width, height, depth;
        private string wallBlock;
        private string floorBlock;
        private string trapBlock;
        private string rewardBlock;
        private float trapChance;
        private float rewardChance;

        private BlockPos origin;

        private int[,] maze;

        public MazeGenerator(ICoreServerAPI api, int width, int height, int depth,
            string wallBlock, string floorBlock, string trapBlock, string rewardBlock,
            float trapChance, float rewardChance)
        {
            this.api = api;
            this.width = width;
            this.height = height;
            this.depth = depth;
            this.wallBlock = wallBlock;
            this.floorBlock = floorBlock;
            this.trapBlock = trapBlock;
            this.rewardBlock = rewardBlock;
            this.trapChance = trapChance;
            this.rewardChance = rewardChance;
        }

        public void Generate(BlockPos origin)
        {
            this.origin = origin;
            maze = new int[width, height];
            GenerateMazeStructure();

            BuildMazeBlocks();
        }

        private void GenerateMazeStructure()
        {
            // Algoritmo simple de generación de laberinto (DFS o Prim simplificado)

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    maze[x, y] = 1; // 1 es pared

            // Hacer un camino simple para demo: solo pasillo central libre
            int mid = height / 2;
            for (int x = 0; x < width; x++)
                maze[x, mid] = 0; // 0 es pasillo libre
        }


        private void BuildMazeBlocks()
        {
            Block wall = api.World.GetBlock(new AssetLocation(wallBlock));
            Block floor = api.World.GetBlock(new AssetLocation(floorBlock));
            Block trap = api.World.GetBlock(new AssetLocation(trapBlock));
            Block reward = api.World.GetBlock(new AssetLocation(rewardBlock));

            Random r = new Random(); // ✅ Solo una instancia

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int cell = maze[x, y];
                    int worldX = origin.X + x;
                    int worldZ = origin.Z + y;
                    int worldY = origin.Y;

                    if (cell == 1)
                    {
                        api.World.BlockAccessor.SetBlock(wall.BlockId, new BlockPos(worldX, worldY, worldZ));
                    }
                    else
                    {
                        api.World.BlockAccessor.SetBlock(floor.BlockId, new BlockPos(worldX, worldY, worldZ));

                        // ✅ Usa solo un valor aleatorio por celda
                        double value = r.NextDouble(); // entre 0.0 y 1.0

                        if (value < trapChance)
                        {
                            api.World.BlockAccessor.SetBlock(trap.BlockId, new BlockPos(worldX, worldY + 1, worldZ));
                        }
                        else if (value < trapChance + rewardChance)
                        {
                            api.World.BlockAccessor.SetBlock(reward.BlockId, new BlockPos(worldX, worldY + 1, worldZ));
                        }
                        // ✅ No pongas nada más si no cae dentro de las chances
                    }
                }
            }
        }

    }
}

