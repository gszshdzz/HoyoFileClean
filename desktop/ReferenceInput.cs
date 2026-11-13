namespace HoyoFileClean.Desktop;

internal sealed record ReferenceInput(LocalManifestFile File, RemoteManifest? Remote = null);
