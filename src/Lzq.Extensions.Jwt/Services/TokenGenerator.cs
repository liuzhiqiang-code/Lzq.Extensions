using Lzq.Core.Interfaces;
using Lzq.Extensions.Jwt.Models;
using Masa.Utils.Security.Token;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Lzq.Extensions.Jwt.Services;

public class TokenGenerator : ITokenGenerator
{
    public TokenResult Generate(ICurrentUser user, TimeSpan timeSpan)
    {
        var claim = new Claim[]
            {
                new Claim("UserId", user.UserId),
                new Claim("UserName", user.UserName??""),
                new Claim("Roles", user.Roles.ToJson()),
                new Claim("Email", user.Email ?? ""),
                new Claim("Sex", user.Sex.ToString() ?? ""),
                new Claim("datetime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                new Claim("token_type", "access"), // 标记Token类型
                new Claim("TenantId", user.TenantId), // 租户id
                new Claim(JwtRegisteredClaimNames.Sid, user.Sid), // Token唯一标识
            };
        var accessToken = JwtUtils.CreateToken(claim, timeSpan);
        return new TokenResult
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = timeSpan.Milliseconds,
        };
    }
}
