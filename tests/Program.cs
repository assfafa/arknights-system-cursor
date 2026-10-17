using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ArknightsSystemCursor
{
    internal static class Tests
    {
        private static int passed;
        private static readonly string SuiteRoot = Path.GetFullPath(Path.Combine("work", "tests", Guid.NewGuid().ToString("N")));

        private static int Main()
        {
            Directory.CreateDirectory(SuiteRoot);
            try
            {
                Run("LZ4 and Arknights overlapping matches", CompressionVectors);
                Run("Truncated and invalid compressed data", InvalidCompression);
                Run("Only mouse bundle is selected", OnlyMouseBundle);
                Run("Enable and restore preserve exact bytes", RoundTrip);
                Run("Restore legacy manual rename", RestoreLegacy);
                Run("Game process blocks mutation", RunningGame);
                Run("Damaged renamed resource blocks restore", DamagedResource);
                Run("Recover when renamed resource is missing", RecoverBackup);
                Run("New game resource is never overwritten", PreserveGameUpdate);
                Run("Re-enable after update restores new version", ReenableAfterUpdate);
                Run("Concurrent operation is rejected", ConcurrentOperation);
                Run("Tampered manifest cannot escape game directory", ManifestTraversal);
                Run("Multiple versions without state are rejected", AmbiguousRestore);
                Run("Different installations use separate state", SeparateInstallations);
                Console.WriteLine("PASS: " + passed + " tests");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            finally
            {
                string intendedParent = Path.GetFullPath(Path.Combine("work", "tests")) + Path.DirectorySeparatorChar;
                if (SuiteRoot.StartsWith(intendedParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(SuiteRoot))
                {
                    Directory.Delete(SuiteRoot, true);
                }
            }
        }

        private static void Run(string name, Action test)
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        private static void CompressionVectors()
        {
            byte[] normal = { 0x35, 97, 98, 99, 3, 0, 0x50, 104, 101, 108, 108, 111 };
            byte[] custom = { 0x53, 97, 98, 99, 0, 3, 0x05, 104, 101, 108, 108, 111 };
            string expected = "abcabcabcabchello";
            Equal(expected, Encoding.ASCII.GetString(BundleInspector.Decode(normal, 17, 2)));
            Equal(expected, Encoding.ASCII.GetString(BundleInspector.Decode(custom, 17, 4)));
            Equal(expected, Encoding.ASCII.GetString(BundleInspector.Decode(custom, 17, 5)));
            byte[] longLiteral = new byte[] { 0x0F, 5 }.Concat(Enumerable.Repeat((byte)65, 20)).ToArray();
            Equal(new string('A', 20), Encoding.ASCII.GetString(BundleInspector.Decode(longLiteral, 20, 5)));
        }

        private static void InvalidCompression()
        {
            Throws(() => BundleInspector.Decode(new byte[] { 0xFF }, 20, 5));
            Throws(() => BundleInspector.Decode(new byte[] { 0, 0, 0 }, 4, 2));
            Throws(() => BundleInspector.Decode(new byte[] { 0x50, 65 }, 5, 2));
            Throws(() => BundleInspector.Decode(new byte[] { 0 }, UInt32.MaxValue, 0));
        }

        private static void OnlyMouseBundle()
        {
            Fixture fixture = NewFixture();
            string unrelated = Path.Combine(Path.GetDirectoryName(fixture.Resource)!, "unrelated.bin");
            File.WriteAllBytes(unrelated, MakeBundle("dyn/ui/other.prefab"));
            byte[] before = File.ReadAllBytes(unrelated);
            Equal(1, fixture.Service.Inspect(fixture.Game).Resources.Count);
            fixture.Service.Enable(fixture.Game);
            Check(before.SequenceEqual(File.ReadAllBytes(unrelated)), "Unrelated asset changed.");
        }

        private static void RoundTrip()
        {
            Fixture fixture = NewFixture();
            byte[] before = File.ReadAllBytes(fixture.Resource);
            OperationResult enabled = fixture.Service.Enable(fixture.Game);
            Equal("system", enabled.Status!.Mode);
            Equal(1, enabled.ChangedFiles);
            Equal(0, fixture.Service.Enable(fixture.Game).ChangedFiles);
            Equal("game", fixture.Service.Restore(fixture.Game).Status!.Mode);
            Check(before.SequenceEqual(File.ReadAllBytes(fixture.Resource)), "Round-trip bytes differ.");
            Equal(0, fixture.Service.Restore(fixture.Game).ChangedFiles);
            Equal("untouched", File.ReadAllText(Path.Combine(fixture.Game, "GameAssembly.dll")));
        }

        private static void RestoreLegacy()
        {
            Fixture fixture = NewFixture();
            File.Move(fixture.Resource, fixture.Resource + CursorService.LegacySuffix);
            Equal("system", fixture.Service.Inspect(fixture.Game).Mode);
            Equal(1, fixture.Service.Restore(fixture.Game).ChangedFiles);
            Check(File.Exists(fixture.Resource), "Legacy resource was not restored.");
        }

        private static void RunningGame()
        {
            Fixture fixture = NewFixture();
            CursorService blocked = new CursorService(fixture.State, () => true);
            Throws(() => blocked.Enable(fixture.Game));
            Check(File.Exists(fixture.Resource), "Running-game guard changed the resource.");
        }

        private static void DamagedResource()
        {
            Fixture fixture = NewFixture();
            CursorStatus status = fixture.Service.Enable(fixture.Game).Status!;
            string renamed = status.Resources.Single().CurrentPath;
            File.AppendAllText(renamed, "corrupt");
            Throws(() => fixture.Service.Restore(fixture.Game));
            Check(!File.Exists(fixture.Resource), "Corrupted source was restored.");
        }

        private static void RecoverBackup()
        {
            Fixture fixture = NewFixture();
            byte[] original = File.ReadAllBytes(fixture.Resource);
            CursorStatus status = fixture.Service.Enable(fixture.Game).Status!;
            File.Delete(status.Resources.Single().CurrentPath);
            Equal("missing", fixture.Service.Inspect(fixture.Game).Mode);
            fixture.Service.Restore(fixture.Game);
            Check(original.SequenceEqual(File.ReadAllBytes(fixture.Resource)), "Backup recovery differs.");
        }

        private static void PreserveGameUpdate()
        {
            Fixture fixture = NewFixture();
            fixture.Service.Enable(fixture.Game);
            byte[] newer = MakeBundle("dyn/mouse/pc_mouse_mgr.prefab", "new-version");
            File.WriteAllBytes(fixture.Resource, newer);
            Equal(0, fixture.Service.Restore(fixture.Game).ChangedFiles);
            Check(newer.SequenceEqual(File.ReadAllBytes(fixture.Resource)), "New game version was overwritten.");
        }

        private static void ReenableAfterUpdate()
        {
            Fixture fixture = NewFixture();
            fixture.Service.Enable(fixture.Game);
            byte[] newer = MakeBundle("dyn/mouse/pc_mouse_mgr.prefab", "new-version");
            File.WriteAllBytes(fixture.Resource, newer);
            fixture.Service.Enable(fixture.Game);
            fixture.Service.Restore(fixture.Game);
            Check(newer.SequenceEqual(File.ReadAllBytes(fixture.Resource)), "Wrong version restored after update.");
        }

        private static void ConcurrentOperation()
        {
            Fixture fixture = NewFixture();
            string directory = fixture.Service.Inspect(fixture.Game).StateDirectory;
            Directory.CreateDirectory(directory);
            using (FileStream locked = new FileStream(Path.Combine(directory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Throws(() => fixture.Service.Enable(fixture.Game));
            }
            Check(File.Exists(fixture.Resource), "Concurrent operation changed original.");
        }

        private static void ManifestTraversal()
        {
            Fixture fixture = NewFixture();
            CursorStatus status = fixture.Service.Enable(fixture.Game).Status!;
            string manifest = Path.Combine(status.StateDirectory, "manifest.json");
            JavaScriptSerializer json = new JavaScriptSerializer();
            InstallState state = json.Deserialize<InstallState>(File.ReadAllText(manifest));
            state.Entries[0].RelativePath = "..\\outside.bin";
            File.WriteAllText(manifest, json.Serialize(state));
            Throws(() => fixture.Service.Restore(fixture.Game));
        }

        private static void AmbiguousRestore()
        {
            Fixture fixture = NewFixture();
            byte[] original = File.ReadAllBytes(fixture.Resource);
            File.Move(fixture.Resource, fixture.Resource + CursorService.DisabledSuffix + "OLD");
            File.WriteAllBytes(fixture.Resource + CursorService.DisabledSuffix + "NEW", original);
            Throws(() => fixture.Service.Restore(fixture.Game));
        }

        private static void SeparateInstallations()
        {
            Fixture first = NewFixture();
            Fixture second = NewFixture();
            CursorService service = new CursorService(first.State, () => false);
            string firstState = service.Inspect(first.Game).StateDirectory;
            string secondState = service.Inspect(second.Game).StateDirectory;
            Check(firstState != secondState, "State directories collide.");
            service.Enable(first.Game);
            Check(File.Exists(second.Resource), "Other installation changed.");
        }

        private static Fixture NewFixture()
        {
            string directory = Path.Combine(SuiteRoot, Guid.NewGuid().ToString("N"));
            string game = Path.Combine(directory, "游戏 folder");
            string resource = Path.Combine(game, "Arknights_Data", "StreamingAssets", "AB", "Windows", "anon", "synthetic.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(resource)!);
            File.WriteAllText(Path.Combine(game, "Arknights.exe"), "synthetic test executable");
            File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "untouched");
            File.WriteAllBytes(resource, MakeBundle("dyn/mouse/pc_mouse_mgr.prefab"));
            string state = Path.Combine(directory, "state");
            return new Fixture { Game = game, Resource = resource, State = state, Service = new CursorService(state, () => false) };
        }

        private static byte[] MakeBundle(string marker, string extra = "")
        {
            byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
            byte[] payload;
            using (MemoryStream body = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(body))
            {
                writer.Write(markerBytes.Length);
                writer.Write(markerBytes);
                writer.Write(Encoding.ASCII.GetBytes(extra));
                payload = body.ToArray();
            }
            byte[] info;
            using (MemoryStream body = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(body))
            {
                writer.Write(new byte[16]);
                WriteBE(writer, 1);
                WriteBE(writer, (uint)payload.Length);
                WriteBE(writer, (uint)payload.Length);
                writer.Write(new byte[2]);
                WriteBE(writer, 0);
                info = body.ToArray();
            }
            using (MemoryStream body = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(body))
            {
                writer.Write(Encoding.ASCII.GetBytes("UnityFS\0"));
                WriteBE(writer, 8);
                writer.Write(Encoding.ASCII.GetBytes("5.x.x\0"));
                writer.Write(Encoding.ASCII.GetBytes("2021.3.39f1\0"));
                long sizeOffset = body.Position;
                WriteBE(writer, 0);
                WriteBE(writer, 0);
                WriteBE(writer, (uint)info.Length);
                WriteBE(writer, (uint)info.Length);
                WriteBE(writer, 64);
                while (body.Position % 16 != 0)
                {
                    writer.Write((byte)0);
                }
                writer.Write(info);
                writer.Write(payload);
                long size = body.Length;
                body.Position = sizeOffset;
                WriteBE(writer, (uint)(size >> 32));
                WriteBE(writer, (uint)size);
                return body.ToArray();
            }
        }

        private static void WriteBE(BinaryWriter writer, uint value)
        {
            writer.Write(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });
        }

        private static void Equal<T>(T expected, T actual)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), "Expected " + expected + "; got " + actual);
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                // Assertion passed.
            }
            else
            {
                throw new Exception(message);
            }
        }

        private static void Throws(Action action)
        {
            bool thrown = false;
            try { action(); }
            catch (Exception) { thrown = true; }
            Check(thrown, "Expected operation to be rejected.");
        }

        private sealed class Fixture
        {
            public string Game = "";
            public string Resource = "";
            public string State = "";
            public CursorService Service = null!;
        }
    }
}
