using AegisScribe.ApiService.Managers.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.ApiService.Data;

public class AegisScribeDbContext(DbContextOptions<AegisScribeDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
}
