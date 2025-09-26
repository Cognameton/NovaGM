using System;
using System.IO;
using QRCoder;

namespace NovaGM.Services
{
    /// <summary>
    /// Service for generating QR codes for room joining
    /// </summary>
    public static class QRCodeService
    {
        /// <summary>
        /// Generate QR code as PNG bytes for the given join URL
        /// </summary>
        public static byte[] GenerateQRCode(string joinUrl, int pixelsPerModule = 20)
        {
            if (string.IsNullOrWhiteSpace(joinUrl))
                throw new ArgumentException("Join URL cannot be empty", nameof(joinUrl));

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(joinUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            
            return qrCode.GetGraphic(pixelsPerModule);
        }

        /// <summary>
        /// Generate QR code as base64 data URL for embedding in HTML
        /// </summary>
        public static string GenerateQRCodeDataUrl(string joinUrl, int pixelsPerModule = 20)
        {
            var pngBytes = GenerateQRCode(joinUrl, pixelsPerModule);
            var base64 = Convert.ToBase64String(pngBytes);
            return $"data:image/png;base64,{base64}";
        }

        /// <summary>
        /// Save QR code to file
        /// </summary>
        public static void SaveQRCodeToFile(string joinUrl, string filePath, int pixelsPerModule = 20)
        {
            var pngBytes = GenerateQRCode(joinUrl, pixelsPerModule);
            File.WriteAllBytes(filePath, pngBytes);
        }
    }
}