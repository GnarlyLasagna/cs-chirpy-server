using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Chirpy
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly TokenService _tokenService;

        public AuthController(TokenService tokenService)
        {
            _tokenService = tokenService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest loginRequest)
        {

            var userId = ValidateUser(loginRequest.Email, loginRequest.Password);

            if (userId == null)
            {
                return Unauthorized(new { error = "Invalid credentials" });
            }

            // Generate JWT token
            var token = _tokenService.GenerateToken(userId);

            return Ok(new { token });
        }

        private string ValidateUser(string email, string password)
        {

            return "some_user_id";
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }
}

