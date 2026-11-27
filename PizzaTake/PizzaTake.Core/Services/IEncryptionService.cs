namespace PizzaTake.Core.Services;

/// <summary>
/// Provides symmetric encryption and decryption for sensitive user data.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
