using Lzq.Core.Interfaces;
using Lzq.Extensions.Jwt.Models;

namespace Lzq.Extensions.Jwt.Services;

public interface ITokenGenerator
{
    TokenResult Generate(ICurrentUser user, TimeSpan expiresIn);
}
