namespace PeripheralAssetTracker.Application.Abstractions;

public interface IReportFileWriter
{
    /// <summary>Writes a text report (UTF-8) and returns its full path.</summary>
    Task<string> WriteAsync(string fileName, string content, CancellationToken cancellationToken = default);
}
