using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NovaGM.Services
{
    public sealed class VectorStoreSqlite
    {
        private readonly string _dbPath;
        private readonly int _dim;

        public VectorStoreSqlite(string dbPath, int dim = 384)
        {
            _dbPath = dbPath;
            _dim = dim;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            EnsureSchema();
        }

        private string ConnStr => $"Data Source={_dbPath};Cache=Shared";

        private void EnsureSchema()
        {
            using var cx = new SqliteConnection(ConnStr);
            cx.Open();
            using var cmd = cx.CreateCommand();
            cmd.CommandText =
            @$"CREATE TABLE IF NOT EXISTS items(
                   id   INTEGER PRIMARY KEY AUTOINCREMENT,
                   text TEXT UNIQUE NOT NULL,
                   dim  INTEGER NOT NULL,
                   vec  BLOB NOT NULL
               );
               CREATE INDEX IF NOT EXISTS ix_items_text ON items(text);";
            cmd.ExecuteNonQuery();
        }

        public async Task UpsertAsync(IEnumerable<string> texts, IEmbedder embedder)
        {
            using var cx = new SqliteConnection(ConnStr);
            await cx.OpenAsync();

            using var tx = cx.BeginTransaction();
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                var v = embedder.Embed(t);
                if (v.Length != _dim) continue;

                byte[] blob = new byte[v.Length * sizeof(float)];
                Buffer.BlockCopy(v, 0, blob, 0, blob.Length);

                var cmd = cx.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR IGNORE INTO items(text, dim, vec) VALUES ($t,$d,$v);";
                cmd.Parameters.AddWithValue("$t", t);
                cmd.Parameters.AddWithValue("$d", _dim);
                cmd.Parameters.Add("$v", SqliteType.Blob).Value = blob;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }

        public async Task<List<(string Text, float Score)>> TopKAsync(float[] q, int k = 6)
        {
            var list = new List<(string Text, float Score)>(k);
            using var cx = new SqliteConnection(ConnStr);
            await cx.OpenAsync();

            var cmd = cx.CreateCommand();
            cmd.CommandText = "SELECT text, vec FROM items;";
            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var text = rd.GetString(0);
                var blob = (byte[])rd[1];
                var v = new float[_dim];
                Buffer.BlockCopy(blob, 0, v, 0, blob.Length);
                float score = Cosine(q, v);
                InsertTopK(list, (text, score), k);
            }
            return list;
        }

        private static void InsertTopK(List<(string Text, float Score)> list, (string Text, float Score) item, int k)
        {
            int i = list.FindIndex(t => item.Score > t.Score);
            if (i < 0) list.Add(item);
            else list.Insert(i, item);
            if (list.Count > k) list.RemoveAt(list.Count - 1);
        }

        private static float Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++) { dot += a[i]*b[i]; na += a[i]*a[i]; nb += b[i]*b[i]; }
            if (na == 0 || nb == 0) return 0f;
            return (float)(dot / Math.Sqrt(na * nb));
        }
    }
}
