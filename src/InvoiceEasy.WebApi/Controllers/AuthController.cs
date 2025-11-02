using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Infrastructure.Services;
using InvoiceEasy.WebApi.DTOs;
using InvoiceEasy.WebApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;

namespace InvoiceEasy.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly JwtService _jwtService;

    public AuthController(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        JwtService jwtService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        if (await _userRepository.EmailExistsAsync(request.Email))
            return BadRequest(new { error = "Email already exists" });

        var user = new Domain.Entities.User
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
            CompanyName = request.CompanyName,
            Locale = "de",
            Plan = "free"
        };

        await _userRepository.AddAsync(user);

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_jwtService.GetRefreshTokenDays());

        await _refreshTokenRepository.AddAsync(new Domain.Entities.RefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = expiresAt
        });

        return Created("/api/auth/register", new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                CompanyName = user.CompanyName,
                Locale = user.Locale,
                Plan = user.Plan
            }
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_jwtService.GetRefreshTokenDays());

        await _refreshTokenRepository.AddAsync(new Domain.Entities.RefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = expiresAt
        });

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                CompanyName = user.CompanyName,
                Locale = user.Locale,
                Plan = user.Plan
            }
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var token = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);

        if (token == null || token.ExpiresAt < DateTime.UtcNow)
            return Unauthorized();

        token.IsRevoked = true;
        await _refreshTokenRepository.UpdateAsync(token);

        var newRefreshToken = _jwtService.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddDays(_jwtService.GetRefreshTokenDays());

        await _refreshTokenRepository.AddAsync(new Domain.Entities.RefreshToken
        {
            UserId = token.UserId,
            Token = newRefreshToken,
            ExpiresAt = expiresAt
        });

        var accessToken = _jwtService.GenerateAccessToken(token.User);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            User = new UserDto
            {
                Id = token.User.Id,
                Email = token.User.Email,
                CompanyName = token.User.CompanyName,
                Locale = token.User.Locale,
                Plan = token.User.Plan
            }
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.GetUserId();
        await _refreshTokenRepository.RevokeAllByUserIdAsync(userId);
        return Ok();
    }
}

