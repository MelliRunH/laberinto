using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace laberinto
{
    public class MazeGenerator
    {
        private readonly ICoreServerAPI api;
        private readonly int width;
        private readonly int height;
        private readonly int wallHeight;
        private readonly string wallBlockCode;
        private readonly string floorBlockCode;
        private readonly string ceilingBlockCode;
        private readonly string algorithm;
        private readonly double trapChance;
        private readonly double rewardChance;
        private bool[,] maze;

        public MazeGenerator(ICoreServerAPI api, int width, int height, int wallHeight, string wallBlockCode, string floorBlockCode, string ceilingBlockCode, string algorithm, double trapChance, double rewardChance)
        {
            this.api = api;
            this.width = width % 2 == 0 ? width + 1 : width;
            this.height = height % 2 == 0 ? height + 1 : height;
            this.wallHeight = wallHeight;
            this.wallBlockCode = wallBlockCode;
            this.floorBlockCode = floorBlockCode;
            this.ceilingBlockCode = ceilingBlockCode;
            this.algorithm = algorithm.ToLowerInvariant();
            this.trapChance = trapChance;
            this.rewardChance = rewardChance;
        }

        public void Generate(BlockPos origin)
        {
            maze = new bool[width, height];

            switch (algorithm)
            {
                case "prim":
                    GenerateWithPrim();
                    break;
                case "kruskal":
                    GenerateWithKruskal();
                    break;
                default:
                    GenerateWithDFS();
                    break;
            }

            PlaceBlocks(origin);
        }

        private void GenerateWithDFS()
        {
            Stack<(int x, int y)> stack = new();
            int startX = 1, startY = 1;
            maze[startX, startY] = true;
            stack.Push((startX, startY));

            int[] dx = { 0, 0, -2, 2 };
            int[] dy = { -2, 2, 0, 0 };

            Random rnd = new();

            while (stack.Count > 0)
            {
                var (x, y) = stack.Peek();
                List<int> dirs = Enumerable.Range(0, 4).OrderBy(_ => rnd.Next()).ToList();
                bool moved = false;

                foreach (int dir in dirs)
                {
                    int nx = x + dx[dir], ny = y + dy[dir];
                    if (IsInside(nx, ny) && !maze[nx, ny])
                    {
                        maze[nx, ny] = true;
                        maze[x + dx[dir] / 2, y + dy[dir] / 2] = true;
                        stack.Push((nx, ny));
                        moved = true;
                        break;
                    }
                }

                if (!moved) stack.Pop();
            }
        }

        private void GenerateWithPrim()
        {
            Random rnd = new();
            List<(int x, int y)> walls = new();
            int startX = 1, startY = 1;
            maze[startX, startY] = true;

            AddWalls(startX, startY, walls);

            while (walls.Count > 0)
            {
                var idx = rnd.Next(walls.Count);
                var (x, y) = walls[idx];
                walls.RemoveAt(idx);

                if (!maze[x, y])
                {
                    List<(int dx, int dy)> directions = new()
                {
                    (0, -2), (0, 2), (-2, 0), (2, 0)
                };

                    foreach (var (dx, dy) in directions.OrderBy(_ => rnd.Next()))
                    {
                        int nx = x + dx, ny = y + dy;
                        if (IsInside(nx, ny) && maze[nx, ny])
                        {
                            maze[x, y] = true;
                            maze[(x + nx) / 2, (y + ny) / 2] = true;
                            AddWalls(x, y, walls);
                            break;
                        }
                    }
                }
            }
        }

        private void AddWalls(int x, int y, List<(int, int)> walls)
        {
            int[,] dirs = { { 0, -2 }, { 0, 2 }, { -2, 0 }, { 2, 0 } };
            foreach (var i in Enumerable.Range(0, 4))
            {
                int nx = x + dirs[i, 0], ny = y + dirs[i, 1];
                if (IsInside(nx, ny) && !maze[nx, ny])
                    walls.Add((nx, ny));
            }
        }

        private void GenerateWithKruskal()
        {
            int cellCount = ((width - 1) / 2) * ((height - 1) / 2);
            int[,] cellIds = new int[width, height];
            int id = 1;

            for (int x = 1; x < width; x += 2)
            {
                for (int y = 1; y < height; y += 2)
                {
                    cellIds[x, y] = id++;
                }
            }

            List<(int x, int y)> walls = new();

            for (int x = 1; x < width - 1; x++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    if ((x % 2 == 1 && y % 2 == 0) || (x % 2 == 0 && y % 2 == 1))
                    {
                        walls.Add((x, y));
                    }
                }
            }

            Random rnd = new();
            while (walls.Count > 0)
            {
                var idx = rnd.Next(walls.Count);
                var (x, y) = walls[idx];
                walls.RemoveAt(idx);

                int x1 = x % 2 == 0 ? x - 1 : x;
                int y1 = y % 2 == 0 ? y - 1 : y;
                int x2 = x % 2 == 0 ? x + 1 : x;
                int y2 = y % 2 == 0 ? y + 1 : y;

                if (!IsInside(x2, y2)) continue;

                int id1 = cellIds[x1, y1];
                int id2 = cellIds[x2, y2];
                if (id1 != id2)
                {
                    cellIds[x, y] = id1;
                    cellIds[x2, y2] = id1;
                    maze[x, y] = maze[x1, y1] = maze[x2, y2] = true;

                    for (int i = 1; i < width; i += 2)
                    {
                        for (int j = 1; j < height; j += 2)
                        {
                            if (cellIds[i, j] == id2)
                                cellIds[i, j] = id1;
                        }
                    }
                }
            }
        }

        private void PlaceBlocks(BlockPos origin)
        {
            var wallBlock = api.World.GetBlock(new AssetLocation(wallBlockCode));
            var floorBlock = api.World.GetBlock(new AssetLocation(floorBlockCode));
            var ceilingBlock = api.World.GetBlock(new AssetLocation(ceilingBlockCode));
            var trapBlock = api.World.GetBlock(new AssetLocation("game:trapdoor-upside"));
            var chestBlock = api.World.GetBlock(new AssetLocation("game:chest"));

            Random rnd = new();

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    BlockPos pos = origin.AddCopy(x, 0, z);

                    // Suelo
                    api.World.BlockAccessor.SetBlock(floorBlock.BlockId, pos);

                    // Paredes y aire
                    if (!maze[x, z])
                    {
                        for (int y = 0; y < wallHeight; y++)
                        {
                            api.World.BlockAccessor.SetBlock(wallBlock.BlockId, pos.UpCopy(y));
                        }
                    }
                    else
                    {
                        for (int y = 1; y <= wallHeight; y++)
                        {
                            api.World.BlockAccessor.SetBlock(0, pos.UpCopy(y)); // aire
                        }

                        // Trampas y cofres
                        if (rnd.NextDouble() < trapChance)
                        {
                            api.World.BlockAccessor.SetBlock(trapBlock.BlockId, pos.UpCopy(1));
                        }
                        else if (rnd.NextDouble() < rewardChance)
                        {
                            api.World.BlockAccessor.SetBlock(chestBlock.BlockId, pos.UpCopy(1));
                        }
                    }

                    // Techo
                    api.World.BlockAccessor.SetBlock(ceilingBlock.BlockId, pos.UpCopy(wallHeight));
                }
            }

            // Entrada
            api.World.BlockAccessor.SetBlock(0, origin.AddCopy(1, 1, 0));
            // Salida
            api.World.BlockAccessor.SetBlock(0, origin.AddCopy(width - 2, 1, height - 1));
        }

        private bool IsInside(int x, int y)
        {
            return x > 0 && x < width && y > 0 && y < height;
        }
    }
}
