namespace RosterGuard.Data;

/// <summary>資料庫的 schema 版本不是本程式支援的版本（例如由較新版的 RosterGuard 建立）。</summary>
public sealed class UnsupportedSchemaException(string message) : InvalidOperationException(message);
