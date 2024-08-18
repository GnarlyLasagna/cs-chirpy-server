using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Chirpy
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly TokenService _tokenService;
        private readonly UserService _userService; // Add a service to fetch user data

        public AuthController(TokenService tokenService, UserService userService)
        {
            _tokenService = tokenService;
            _userService = userService; // Initialize UserService
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest loginRequest)
        {
            var user = ValidateUser(loginRequest.Email, loginRequest.Password);

            if (user == null)
            {
                return Unauthorized(new { error = "Invalid credentials" });
            }

            // Generate JWT token
            var token = _tokenService.GenerateToken(user.ID.ToString());

            return Ok(new
            {
                email = user.Email,
                id = user.ID,
                is_chirpy_red = user.IsChirpyRed,
                token
            });
        }

        private User ValidateUser(string email, string password)
        {
            // Fetch user details from your data source based on email and password
            // For example:
            return _userService.GetUserByEmailAndPassword(email, password);
        }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class UserService
    {
        public User GetUserByEmailAndPassword(string email, string password)
        {
            // Replace with actual data fetching logic
            // Example stub
            return new User
            {
                ID = 1,
                Email = email,
            };
        }
    }
}

