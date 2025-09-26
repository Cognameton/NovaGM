using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace NovaGM.Services.Retrieval
{
    /// <summary>
    /// Minimal SQLite-backed vector store.
    /// Schema: vec(id INTEGER PK, text TEXT UNIQUE, vec BLOB) — vec is float32[dim] as raw bytes.
    /// </summary>
    public sealed class VectorStoreSqlite
    {
        private readonly string _dbPath;
        private readonly int _dim;

        public VectorStoreSqlite(string dbPath, int dim)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            _dim = dim > 0 ? dim : throw new ArgumentOutOfRangeException(nameof(dim));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_dbPath)) ?? ".");
            EnsureSchema();
        }

        private string ConnStr => new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();

        private void EnsureSchema()
        {
            using var c = new SqliteConnection(ConnStr);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS vec(
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                text TEXT NOT NULL UNIQUE,
                vec  BLOB NOT NULL
            );
            """;
            cmd.ExecuteNonQuery();
        }

        public void UpsertMany(IEnumerable<(string text, float[] vector)> rows)
        {
            using var c = new SqliteConnection(ConnStr);
            c.Open();
            using var tx = c.BeginTransaction();

            foreach (var (text, vector) in rows)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (vector == null || vector.Length != _dim) continue;

                using var cmd = c.CreateCommand();
                cmd.CommandText =
                """
                INSERT INTO vec(text, vec) VALUES($t, $v)
                ON CONFLICT(text) DO UPDATE SET vec = excluded.vec;
                """;
                cmd.Parameters.AddWithValue("$t", text);
                cmd.Parameters.AddWithValue("$v", FloatArrayToBytes(vector));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public List<(string text, float[] vec)> LoadAll()
        {
            var list = new List<(string, float[])>();
            using var c = new SqliteConnection(ConnStr);
            c.Open();

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT text, vec FROM vec;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = r.GetString(0);
                var blob = (byte[])r["vec"];
                var vec = BytesToFloatArray(blob, _dim);
                list.Add((text, vec));
            }
            return list;
        }

        private static byte[] FloatArrayToBytes(float[] v)
        {
            var bytes = new byte[v.Length * sizeof(float)];
            Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] BytesToFloatArray(byte[] bytes, int dim)
        {
            var v = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
            if (v.Length != dim) Array.Resize(ref v, dim);
            return v;
        }
    }
}
