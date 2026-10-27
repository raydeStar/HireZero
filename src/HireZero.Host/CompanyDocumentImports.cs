using System.Security.Cryptography;
using System.Text.Json;
using HireZero.Core;
using Microsoft.AspNetCore.DataProtection;

public sealed class CompanyDocumentImports(IDataProtectionProvider protection, string companyId)
{
    private readonly IDataProtector _protector = protection.CreateProtector("HireZero.DocumentImport.v1", companyId);

    public string Seal(CompanyDocumentSource source) => _protector.Protect(CompanyJson.Encode(source));

    public CompanyDocumentSource Verify(string receipt)
    {
        try
        {
            if (receipt.Length > 4096)
                throw new CryptographicException();
            return CompanyJson.Decode<CompanyDocumentSource>(_protector.Unprotect(receipt));
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            throw new WorkspaceException(
                "This import receipt is invalid for this company. Preview the source again; your draft is preserved.",
                409
            );
        }
    }
}
