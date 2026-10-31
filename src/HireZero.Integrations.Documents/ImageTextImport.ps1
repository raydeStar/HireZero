$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
$null = [Windows.Storage.Streams.DataWriter, Windows.Storage.Streams, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function Await-Ocr($Operation, $ResultType) {
    $task = $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $task.GetAwaiter().GetResult()
}
try {
    $bytes = [Convert]::FromBase64String([Console]::In.ReadToEnd())
    $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    $writer = New-Object Windows.Storage.Streams.DataWriter($stream)
    $writer.WriteBytes($bytes)
    $null = Await-Ocr $writer.StoreAsync() ([uint32])
    $writer.DetachStream() | Out-Null
    $writer.Dispose()
    $stream.Seek(0)
    $decoder = Await-Ocr ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $max = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    if ($decoder.PixelWidth -gt $max -or $decoder.PixelHeight -gt $max -or ([long]$decoder.PixelWidth * $decoder.PixelHeight) -gt 16000000) { throw 'Image dimensions exceed local OCR limits. Crop the screenshot first.' }
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if ($null -eq $engine) { throw 'No Windows OCR language is available. Paste the relevant text instead.' }
    $bitmap = Await-Ocr ($decoder.GetSoftwareBitmapAsync([Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8, [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied)) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Await-Ocr ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $text = ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
    if ([string]::IsNullOrWhiteSpace($text)) { throw 'No readable text found. Try a clearer crop or type what the image shows.' }
    if ([Text.Encoding]::UTF8.GetByteCount($text) -gt 65536) { throw 'Too much recognized text. Crop the screenshot first.' }
    [Console]::Write((@{ text = $text } | ConvertTo-Json -Compress))
} catch {
    [Console]::Write('{"error":"Local OCR could not read this image. Use a clear PNG or JPEG crop, or paste its text."}')
} finally {
    if ($null -ne $bitmap) { $bitmap.Dispose() }
    if ($null -ne $stream) { $stream.Dispose() }
}
