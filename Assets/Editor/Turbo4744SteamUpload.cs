using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GameSystems.Management;
using GameSystems.Modding.Authoring.GameSystems.Modding.Editor;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;
using UnityEditor;
using UnityEngine;

/// Explicit command-line update of the existing item; listing fields are left unset.
public static class Turbo4744SteamUpload
{
    private const ulong ItemId = 3843685878UL;

    [Serializable]
    private sealed class ContentFile
    {
        public string relativePath;
        public string sha256;
    }

    [Serializable]
    private sealed class Status
    {
        public bool release_ready;
        public string version;
        public ContentFile[] files;
    }

    [Serializable]
    private sealed class Listing
    {
        public string id;
        public string title;
        public string descriptionSha256;
        public string[] tags;
        public bool isPublic;
        public string version;
    }

    [Serializable]
    private sealed class Receipt
    {
        public string itemId = ItemId.ToString();
        public string version = "1.0.1";
        public string startedUtc = DateTime.UtcNow.ToString("o");
        public string finishedUtc;
        public bool submitted;
        public bool success;
        public bool needsWorkshopAgreement;
        public bool remoteVerified;
        public string result;
        public string error;
        public ContentFile[] files;
        public Listing before;
        public Listing after;
    }

    private static string Artifacts =>
        Path.GetFullPath(Application.dataPath + "/../BuildArtifacts");

    private static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static Listing Snapshot(Item item)
    {
        string version = null;
        item.KeyValueTags?.TryGetValue(WorkshopPublisher.VersionTagKey, out version);
        return new Listing
        {
            id = item.Id.Value.ToString(),
            title = item.Title,
            descriptionSha256 = Hash(Encoding.UTF8.GetBytes(item.Description ?? "")),
            tags = (item.Tags ?? Array.Empty<string>()).OrderBy(tag => tag).ToArray(),
            isPublic = item.IsPublic,
            version = version,
        };
    }

    private static async Task<Listing> ReadListing()
    {
        var query = Query
            .Items.WithFileId(new PublishedFileId[] { ItemId })
            .WithKeyValueTags(true)
            .AllowCachedResponse(0)
            .GetPageAsync(1);
        if (await Task.WhenAny(query, Task.Delay(TimeSpan.FromSeconds(30))) != query)
            throw new TimeoutException("Steam item query timed out.");
        var result = await query;
        if (!result.HasValue)
            throw new InvalidOperationException("Steam returned no item query result.");
        using var page = result.Value;
        var item = page.Entries.Single();
        if (item.Id.Value != ItemId || (uint)item.ConsumerApp != 4398690U)
            throw new InvalidOperationException("Steam query returned a different item or app.");
        return Snapshot(item);
    }

    private static Status ValidateContent(string staging)
    {
        var status = JsonUtility.FromJson<Status>(
            File.ReadAllText(Artifacts + "/steam-release-status.json")
        );
        if (!status.release_ready || status.version != "1.0.1" || status.files?.Length != 12)
            throw new InvalidOperationException(
                "A certified three-platform content manifest is required."
            );
        var actual = Directory
            .GetFiles(staging, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(staging, path).Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();
        if (
            !actual.SequenceEqual(
                status.files.Select(file => file.relativePath).OrderBy(path => path)
            )
        )
            throw new InvalidOperationException(
                "Staged file inventory changed after certification."
            );
        foreach (var file in status.files)
            if (Hash(File.ReadAllBytes(Path.Combine(staging, file.relativePath))) != file.sha256)
                throw new InvalidOperationException("Staged content changed: " + file.relativePath);
        return status;
    }

    public static async void Inspect()
    {
        int exitCode = 1;
        try
        {
            if (!SteamEditorSession.TryOpen(out var session, out var error))
                throw new InvalidOperationException(error);
            using (session)
            {
                var listing = await ReadListing();
                File.WriteAllText(
                    Artifacts + "/steam-live-item.json",
                    JsonUtility.ToJson(listing, true) + "\n"
                );
                Debug.Log("TURBO_STEAM_INSPECT " + JsonUtility.ToJson(listing));
                exitCode = 0;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorApplication.Exit(exitCode);
        }
    }

    public static async void Run()
    {
        var receipt = new Receipt();
        int exitCode = 1;
        void Save() =>
            File.WriteAllText(
                Artifacts + "/steam-upload-receipt.json",
                JsonUtility.ToJson(receipt, true) + "\n"
            );
        try
        {
            if (!Environment.GetCommandLineArgs().Contains("-turboPublishSteam101"))
                throw new InvalidOperationException("Explicit -turboPublishSteam101 is required.");
            Turbo4744SteamRelease.Prepare();
            var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(Ninjas4744Builder.PackPath);
            var staging = ModBuildUtility.GetStagingPath(pack);
            receipt.files = ValidateContent(staging).files;
            if (!SteamEditorSession.TryOpen(out var session, out var error))
                throw new InvalidOperationException(error);
            using (session)
            {
                receipt.before = await ReadListing();
                Save();
                var editor = new Steamworks.Ugc.Editor(ItemId)
                    .WithContent(staging)
                    .WithChangeLog(Turbo4744SteamRelease.ChangeNote)
                    .RemoveKeyValueTags(WorkshopPublisher.VersionTagKey)
                    .AddKeyValueTag(WorkshopPublisher.VersionTagKey, "1.0.1");
                receipt.submitted = true;
                Save();
                var upload = editor.SubmitAsync(
                    new Progress<float>(value => Debug.Log("TURBO_STEAM_PROGRESS " + value))
                );
                if (await Task.WhenAny(upload, Task.Delay(TimeSpan.FromMinutes(5))) != upload)
                    throw new TimeoutException(
                        "Upload outcome unknown; inspect Steam before retrying."
                    );
                var result = await upload;
                receipt.success = result.Success && result.FileId.Value == ItemId;
                receipt.result = result.Result.ToString();
                receipt.needsWorkshopAgreement = result.NeedsWorkshopAgreement;
                Save();
                if (!receipt.success)
                    throw new InvalidOperationException(
                        "Steam rejected the update: " + receipt.result
                    );
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    receipt.after = await ReadListing();
                    receipt.remoteVerified =
                        receipt.after.version == "1.0.1"
                        && receipt.after.title == receipt.before.title
                        && receipt.after.descriptionSha256 == receipt.before.descriptionSha256
                        && receipt.after.isPublic == receipt.before.isPublic
                        && receipt.after.tags.SequenceEqual(receipt.before.tags);
                    Save();
                    if (receipt.remoteVerified)
                        break;
                    await Task.Delay(5000);
                }
                if (!receipt.remoteVerified)
                    throw new InvalidOperationException(
                        "Upload succeeded; remote metadata verification is incomplete. Do not resubmit automatically."
                    );
                Debug.Log("TURBO_STEAM_UPLOADED " + ItemId);
                exitCode = 0;
            }
        }
        catch (Exception exception)
        {
            receipt.error = exception.ToString();
            Debug.LogException(exception);
        }
        finally
        {
            receipt.finishedUtc = DateTime.UtcNow.ToString("o");
            Save();
            EditorApplication.Exit(exitCode);
        }
    }
}
