namespace StepUp.Eval;

/// <summary>Embeddings take minutes to compute, so a run keeps them under the git-ignored data folder. The key names the model and the design version.</summary>
public sealed class Cache(string directory)
{
    public const int Version = 1;

    public string PathFor(string set, string speaker) => Path.Combine(directory, $"{set}-{speaker}.emb");

    public bool TryRead(string set, string speaker, out SpeakerEmbeddings? embeddings)
    {
        embeddings = null;
        var path = PathFor(set, speaker);
        if (!File.Exists(path))
        {
            return false;
        }

        using var r = new BinaryReader(File.OpenRead(path));
        if (r.ReadInt32() != Version)
        {
            return false;
        }

        var index = r.ReadInt32();
        var id = r.ReadString();
        var redraws = r.ReadInt32();
        var primary = ReadGrid(r);
        var secondary = ReadGrid(r);
        var probes = new Dictionary<string, float[]?[]>();
        var conditions = r.ReadInt32();
        for (var c = 0; c < conditions; c++)
        {
            var name = r.ReadString();
            probes[name] = [.. Enumerable.Range(0, r.ReadInt32()).Select(_ => ReadEmbedding(r))];
        }

        embeddings = new SpeakerEmbeddings { Index = index, Id = id, Primary = primary, Secondary = secondary, Probes = probes, SegmentRedraws = redraws };
        return true;
    }

    public void Write(string set, SpeakerEmbeddings e)
    {
        Directory.CreateDirectory(directory);
        using var w = new BinaryWriter(File.Create(PathFor(set, e.Id)));
        w.Write(Version);
        w.Write(e.Index);
        w.Write(e.Id);
        w.Write(e.SegmentRedraws);
        WriteGrid(w, e.Primary);
        WriteGrid(w, e.Secondary);
        w.Write(e.Probes.Count);
        foreach (var (name, list) in e.Probes)
        {
            w.Write(name);
            w.Write(list.Length);
            foreach (var embedding in list)
            {
                WriteEmbedding(w, embedding);
            }
        }
    }

    private static float[]?[][] ReadGrid(BinaryReader r) =>
        [.. Enumerable.Range(0, r.ReadInt32()).Select(_ => (float[]?[])[.. Enumerable.Range(0, r.ReadInt32()).Select(_ => ReadEmbedding(r))])];

    private static void WriteGrid(BinaryWriter w, float[]?[][] grid)
    {
        w.Write(grid.Length);
        foreach (var row in grid)
        {
            w.Write(row.Length);
            foreach (var e in row)
            {
                WriteEmbedding(w, e);
            }
        }
    }

    private static float[]? ReadEmbedding(BinaryReader r)
    {
        var length = r.ReadInt32();
        return length < 0 ? null : [.. Enumerable.Range(0, length).Select(_ => r.ReadSingle())];
    }

    private static void WriteEmbedding(BinaryWriter w, float[]? e)
    {
        if (e is null)
        {
            w.Write(-1);
            return;
        }

        w.Write(e.Length);
        foreach (var v in e)
        {
            w.Write(v);
        }
    }
}
