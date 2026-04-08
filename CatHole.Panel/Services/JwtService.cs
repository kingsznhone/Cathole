// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CatHole.Panel.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using CatHole.Panel.Models;
namespace CatHole.Panel.Services
{
    public class JwtService
    {
        private readonly JwtSettings _jwtSetting;
        private readonly byte[] _jwtKey;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<JwtService> _logger;

        public JwtService(
            UserManager<ApplicationUser> userManager,
            ILogger<JwtService> logger,
            IOptionsSnapshot<JwtSettings> option
            )
        {
            _userManager = userManager;
            _logger = logger;
            _jwtSetting = option.Value;
            if (string.IsNullOrWhiteSpace(_jwtSetting.Key))
                throw new InvalidOperationException("JwtSettings:Key is not configured. Add a hex-encoded secret key (≥32 hex chars) to appsettings.");
            _jwtKey = Convert.FromHexString(_jwtSetting.Key);
        }

        public string BuildAccessToken(ApplicationUser user,TimeSpan duration)
        {
            var TokenHandler = new JwtSecurityTokenHandler();
            var TokenKey = new SymmetricSecurityKey(_jwtKey);
            List<Claim> claims = [
                new(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.SerialNumber, Guid.NewGuid().ToString()),
                new Claim("scope", "access-token")
                ];

            var TokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.Add(duration),
                SigningCredentials = new SigningCredentials(TokenKey, SecurityAlgorithms.HmacSha256)
            };

            var token = TokenHandler.CreateToken(TokenDescriptor);
            var tokenStr = TokenHandler.WriteToken(token);
            return tokenStr;
        }

        public async Task<TokenValidateResponse> ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_jwtKey),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var result = await tokenHandler.ValidateTokenAsync(token, validationParameters);
            if (result.IsValid)
            {
                if (!result.Claims.Any(x => x.Key == "scope" && (string)x.Value == "access-token"))
                {
                    _logger.LogWarning("Token validation failed: Not an access token");
                    return new TokenValidateResponse { Success = false, Error = "Invalid token type" };
                }

                return new TokenValidateResponse { Success = true };
            }
            else
            {
                if (result.Exception is SecurityTokenException)
                {
                    _logger.LogDebug("Token validation failed");
                    return new TokenValidateResponse { Success = false, Error = result.Exception.Message };
                }
                else
                {
                    _logger.LogWarning("Unknow Token validation failed");
                    return new TokenValidateResponse { Success = false, Error = "Unknow Token Validation Error" };
                }
            }
        }

    }
}
