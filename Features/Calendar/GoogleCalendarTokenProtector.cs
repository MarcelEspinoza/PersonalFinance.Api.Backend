using System.Security.Cryptography;
using System.Text;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed class GoogleCalendarTokenProtector(GoogleCalendarSettings settings)
    {
        public string Protect(string value, Guid userId)
        {
            var key = settings.EncryptionKey();
            var plaintext = Encoding.UTF8.GetBytes(value);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, Purpose(userId));
                return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public string Unprotect(string value, Guid userId)
        {
            var key = settings.EncryptionKey();
            byte[]? plaintext = null;
            try
            {
                var data = Convert.FromBase64String(value);
                if (data.Length < 28) throw new CryptographicException("Invalid Google credential.");
                plaintext = new byte[data.Length - 28];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plaintext, Purpose(userId));
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("Invalid Google credential.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static byte[] Purpose(Guid userId) => Encoding.UTF8.GetBytes("google-calendar:" + userId.ToString("N"));
    }
}
