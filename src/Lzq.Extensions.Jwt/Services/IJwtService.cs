using Lzq.Core.Interfaces;
using Lzq.Extensions.Jwt.Models;

namespace Lzq.Extensions.Jwt.Services;

public interface IJwtService
{
    TokenViewDto GenerateToken(ICurrentUser user, TimeSpan timeSpan);
}
