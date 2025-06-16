using System;
using System.Collections.Generic;
using System.IO;

class Program
{
    static void Main()
    {
        const int viewportWidth = 40;
        const int viewportHeight = 20;
        string worldFolder = "world";
        string levelPath = Path.Combine(worldFolder, "level.dat");
        Directory.CreateDirectory(worldFolder);

        int playerX, playerY, seed;
        if (File.Exists(levelPath))
        {
            // Load player position and seed
            var lines = File.ReadAllLines(levelPath);
            try
            {
                playerX = int.Parse(lines[0]);
            }
            catch (OverflowException)
            {
                if (lines[0].StartsWith("-"))
                    playerX = int.MinValue;
                else
                    playerX = int.MaxValue;
            }
            try
            {
                playerY = int.Parse(lines[1]);
            }
            catch (OverflowException)
            {
                if (lines[1].StartsWith("-"))
                    playerY = int.MinValue;
                else
                    playerY = int.MaxValue;
            }
            seed = int.Parse(lines[2]);
        }
        else
        {
            // Generate new world
            var rng = new Random();
            seed = rng.Next();
            playerX = 0;
            playerY = 0;
            File.WriteAllLines(levelPath, new[] { playerX.ToString(), playerY.ToString(), seed.ToString() });
        }

        var world = new World(worldFolder, seed);
        var player = new Player(playerX, playerY);

        ConsoleKey key;
        bool commandMode = false;
        do
        {
            Console.Clear();
            // Show player coordinates and chunk info
            int chunkX = World.FloorDiv(player.X, 16);
            int chunkY = World.FloorDiv(player.Y, 16);
            Console.WriteLine($"Pos: X={player.X} Y={player.Y} | Chunk: {chunkX},{chunkY}");
            Console.WriteLine(new string('-', viewportWidth));
            world.Render(player, viewportWidth, viewportHeight);
            if (!commandMode)
            {
                Console.WriteLine("Use WASD to move, T for command, Q to quit.");
                key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.W: player.Move(0, -1); break;
                    case ConsoleKey.S: player.Move(0, 1); break;
                    case ConsoleKey.A: player.Move(-1, 0); break;
                    case ConsoleKey.D: player.Move(1, 0); break;
                    case ConsoleKey.T: commandMode = true; break;
                }
            }
            else
            {
                Console.Write("Command> ");
                string? cmd = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    var parts = cmd.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && parts[0].ToLower() == "tp"
                        && int.TryParse(parts[1], out int tx)
                        && int.TryParse(parts[2], out int ty))
                    {
                        player.X = tx;
                        player.Y = ty;
                        Console.WriteLine($"Teleported to {tx},{ty}.");
                    }
                    // --- Begin: gen radius command ---
                    else if (parts.Length == 2 && parts[0].ToLower() == "gen"
                        && int.TryParse(parts[1], out int radius) && radius > 0)
                    {
                        int centerChunkX = World.FloorDiv(player.X, 16);
                        int centerChunkY = World.FloorDiv(player.Y, 16);
                        int total = 0, created = 0;
                        for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int cx = centerChunkX + dx;
                            int cy = centerChunkY + dy;
                            total++;
                            string chunkPath = world.ChunkPath(cx, cy);
                            if (!File.Exists(chunkPath))
                            {
                                var chunk = Chunk.Generate(cx, cy, world.GetNoise());
                                chunk.Save(chunkPath);
                                created++;
                            }
                            if (total % 50 == 0)
                            {
                                Console.WriteLine($"Progress: {total} chunks checked, {created} generated...");
                            }
                        }
                        Console.WriteLine($"Done. {created} new chunks generated in radius {radius}.");
                    }
                    // --- End: gen radius command ---
                    else
                    {
                        Console.WriteLine("Unknown or invalid command.");
                    }
                }
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
                commandMode = false;
                key = 0; // continue loop
            }
        } while (key != ConsoleKey.Q);

        // Save player position and seed on exit
        File.WriteAllLines(levelPath, new[] { player.X.ToString(), player.Y.ToString(), seed.ToString() });
    }
}

class Player
{
    public int X, Y; // absolute world coords
    public Player(int x, int y) { X = x; Y = y; }
    public void Move(int dx, int dy)
    {
        X += dx; Y += dy;
    }
}

class World
{
    const int ChunkSize = 16;
    private readonly string worldFolder;
    private readonly Dictionary<(int, int), Chunk> chunks = new();
    private readonly PerlinNoise noise;
    private readonly int seed;

    public World(string folder, int seed)
    {
        worldFolder = folder;
        this.seed = seed;
        Directory.CreateDirectory(worldFolder);
        noise = new PerlinNoise(seed);
    }

    public void Render(Player player, int viewportWidth, int viewportHeight)
    {
        int halfW = viewportWidth / 2, halfH = viewportHeight / 2;
        int topLeftX = player.X - halfW;
        int topLeftY = player.Y - halfH;

        for (int y = 0; y < viewportHeight; y++)
        {
            for (int x = 0; x < viewportWidth; x++)
            {
                int wx = topLeftX + x;
                int wy = topLeftY + y;
                if (wx == player.X && wy == player.Y)
                {
                    Console.Write('@');
                }
                else
                {
                    Console.Write(GetTile(wx, wy));
                }
            }
            Console.WriteLine();
        }
    }

    char GetTile(int wx, int wy)
    {
        int cx = FloorDiv(wx, ChunkSize), cy = FloorDiv(wy, ChunkSize);
        int lx = Mod(wx, ChunkSize), ly = Mod(wy, ChunkSize);
        var chunk = GetOrLoadChunk(cx, cy);
        return chunk.Tiles[lx, ly];
    }

    Chunk GetOrLoadChunk(int cx, int cy)
    {
        var key = (cx, cy);
        if (chunks.TryGetValue(key, out var chunk))
            return chunk;
        string path = ChunkPath(cx, cy);
        if (File.Exists(path))
        {
            chunk = Chunk.Load(path);
        }
        else
        {
            chunk = Chunk.Generate(cx, cy, noise);
            chunk.Save(path);
        }
        chunks[key] = chunk;
        return chunk;
    }

    public string ChunkPath(int cx, int cy) => Path.Combine(worldFolder, $"chunk_{cx}_{cy}.dat");
    public PerlinNoise GetNoise() => noise;

    public static int FloorDiv(int a, int b) => (a >= 0) ? a / b : ((a + 1) / b) - 1;
    static int Mod(int a, int b) => ((a % b) + b) % b;
}

class Chunk
{
    public char[,] Tiles = new char[16, 16];

    public static Chunk Generate(int cx, int cy, PerlinNoise noise)
    {
        var chunk = new Chunk();
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int wx = cx * 16 + x, wy = cy * 16 + y;
            double n = noise.Noise(wx * 0.1, wy * 0.1, 0);
            if (n < -0.2) chunk.Tiles[x, y] = '~';
            else if (n < 0.1) chunk.Tiles[x, y] = ',';
            else chunk.Tiles[x, y] = '#';
        }
        return chunk;
    }

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            fs.WriteByte((byte)Tiles[x, y]);
    }

    public static Chunk Load(string path)
    {
        var chunk = new Chunk();
        var bytes = File.ReadAllBytes(path);
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            chunk.Tiles[x, y] = (char)bytes[y * 16 + x];
        return chunk;
    }
}

// Simple Perlin noise implementation
class PerlinNoise
{
    private readonly int[] p;
    public PerlinNoise(int? seed = null)
    {
        Random rand = seed.HasValue ? new Random(seed.Value) : new Random();
        p = new int[512];
        var perm = new int[256];
        for (int i = 0; i < 256; i++) perm[i] = i;
        for (int i = 0; i < 256; i++)
        {
            int j = rand.Next(256);
            int tmp = perm[i]; perm[i] = perm[j]; perm[j] = tmp;
        }
        for (int i = 0; i < 512; i++) p[i] = perm[i & 255];
    }

    public double Noise(double x, double y, double z)
    {
        int X = (int)Math.Floor(x) & 255,
            Y = (int)Math.Floor(y) & 255,
            Z = (int)Math.Floor(z) & 255;
        x -= Math.Floor(x);
        y -= Math.Floor(y);
        z -= Math.Floor(z);
        double u = Fade(x), v = Fade(y), w = Fade(z);
        int A = p[X] + Y, AA = p[A] + Z, AB = p[A + 1] + Z,
            B = p[X + 1] + Y, BA = p[B] + Z, BB = p[B + 1] + Z;

        return Lerp(w, Lerp(v, Lerp(u, Grad(p[AA], x, y, z),
                                      Grad(p[BA], x - 1, y, z)),
                              Lerp(u, Grad(p[AB], x, y - 1, z),
                                      Grad(p[BB], x - 1, y - 1, z))),
                      Lerp(v, Lerp(u, Grad(p[AA + 1], x, y, z - 1),
                                      Grad(p[BA + 1], x - 1, y, z - 1)),
                              Lerp(u, Grad(p[AB + 1], x, y - 1, z - 1),
                                      Grad(p[BB + 1], x - 1, y - 1, z - 1))));
    }

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    static double Lerp(double t, double a, double b) => a + t * (b - a);
    static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y,
               v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) +
               ((h & 2) == 0 ? v : -v);
    }
}
