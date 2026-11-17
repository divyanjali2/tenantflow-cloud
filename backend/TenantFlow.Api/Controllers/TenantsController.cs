using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants")]
public sealed class TenantsController(
    ApplicationDbContext dbContext,
    ITenantAuthorizationService tenantAuthorizationService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private static readonly string[] MemberListingRoles = [TenantRoles.Owner, TenantRoles.Admin];
    private static readonly string[] InvitationRoles = [TenantRoles.Owner];
    private static readonly string[] OwnerOnlyRoles = [TenantRoles.Owner];

    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<TenantListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TenantListItemResponse>>> GetMine(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var memberships = await dbContext.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Select(membership => new
            {
                membership.Tenant.Id,
                membership.Tenant.Name,
                membership.Tenant.Slug,
                membership.Role,
                membership.Tenant.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var tenants = memberships
            .Select(membership => new TenantListItemResponse(
                membership.Id,
                membership.Name,
                membership.Slug,
                membership.Role,
                new DateTimeOffset(DateTime.SpecifyKind(membership.CreatedAtUtc, DateTimeKind.Utc))))
            .ToArray();

        return Ok(tenants);
    }

    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType<TenantDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantDetailsResponse>> GetById(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var tenant = await dbContext.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => new
            {
                membership.Tenant.Id,
                membership.Tenant.Name,
                membership.Tenant.Slug,
                membership.Role,
                membership.Tenant.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
        {
            return NotFound();
        }

        return Ok(new TenantDetailsResponse(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Role,
            new DateTimeOffset(DateTime.SpecifyKind(tenant.CreatedAtUtc, DateTimeKind.Utc))));
    }

    [HttpPost("{tenantId:guid}/select")]
    [ProducesResponseType<CreateTenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateTenantResponse>> Select(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var tenant = await dbContext.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.TenantId == tenantId)
            .Select(membership => new CreateTenantResponse(
                membership.Tenant.Id,
                membership.Tenant.Name,
                membership.Tenant.Slug,
                membership.Role))
            .FirstOrDefaultAsync(cancellationToken);

        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpGet("{tenantId:guid}/members")]
    [ProducesResponseType<IReadOnlyList<TenantMemberResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TenantMemberResponse>>> GetMembers(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                userId,
                tenantId,
                MemberListingRoles,
                cancellationToken))
        {
            return Forbid();
        }

        var members = await dbContext.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => new TenantMemberResponse(
                membership.UserId,
                membership.User.Email!,
                membership.Role,
                new DateTimeOffset(DateTime.SpecifyKind(membership.CreatedAtUtc, DateTimeKind.Utc))))
            .ToListAsync(cancellationToken);

        return Ok(members);
    }

    [HttpPatch("{tenantId:guid}/members/{userId:guid}/role")]
    [ProducesResponseType<TenantMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantMemberResponse>> UpdateMemberRole(
        Guid tenantId,
        Guid userId,
        UpdateTenantMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(
                currentUserId,
                tenantId,
                cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                currentUserId,
                tenantId,
                OwnerOnlyRoles,
                cancellationToken))
        {
            return Forbid();
        }

        if (request.Role is not TenantRoles.Admin and not TenantRoles.Member)
        {
            ModelState.AddModelError(
                nameof(request.Role),
                "Role must be Admin or Member.");
            return ValidationProblem(ModelState);
        }

        var membership = await dbContext.TenantMemberships
            .Include(entry => entry.User)
            .FirstOrDefaultAsync(
                entry => entry.TenantId == tenantId && entry.UserId == userId,
                cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        if (membership.Role == TenantRoles.Owner || userId == currentUserId)
        {
            ModelState.AddModelError(
                nameof(request.Role),
                "The tenant Owner's role cannot be changed.");
            return ValidationProblem(ModelState);
        }

        membership.Role = request.Role;
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new TenantMemberResponse(
            membership.UserId,
            membership.User.Email!,
            membership.Role,
            new DateTimeOffset(DateTime.SpecifyKind(membership.CreatedAtUtc, DateTimeKind.Utc)));
        return Ok(response);
    }

    [HttpDelete("{tenantId:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(
                currentUserId,
                tenantId,
                cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                currentUserId,
                tenantId,
                OwnerOnlyRoles,
                cancellationToken))
        {
            return Forbid();
        }

        var membership = await dbContext.TenantMemberships
            .FirstOrDefaultAsync(
                entry => entry.TenantId == tenantId && entry.UserId == userId,
                cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        if (membership.Role == TenantRoles.Owner || userId == currentUserId)
        {
            ModelState.AddModelError(
                nameof(userId),
                "The tenant Owner cannot be removed.");
            return ValidationProblem(ModelState);
        }

        dbContext.TenantMemberships.Remove(membership);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{tenantId:guid}/invitations")]
    [ProducesResponseType<TenantMemberResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TenantMemberResponse>> CreateInvitation(
        Guid tenantId,
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                userId,
                tenantId,
                InvitationRoles,
                cancellationToken))
        {
            return Forbid();
        }

        if (request.Role is not TenantRoles.Member and not TenantRoles.Admin)
        {
            ModelState.AddModelError(
                nameof(request.Role),
                "Role must be Member or Admin.");
            return ValidationProblem(ModelState);
        }

        var invitedUser = await userManager.FindByEmailAsync(request.Email);
        if (invitedUser?.Email is null)
        {
            return NotFound();
        }

        var alreadyMember = await dbContext.TenantMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.TenantId == tenantId && membership.UserId == invitedUser.Id,
                cancellationToken);
        if (alreadyMember)
        {
            return MembershipConflict();
        }

        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = invitedUser.Id,
            Role = request.Role,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.TenantMemberships.Add(membership);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTenantMembershipUniqueViolation(exception))
        {
            return MembershipConflict();
        }

        var response = new TenantMemberResponse(
            invitedUser.Id,
            invitedUser.Email,
            membership.Role,
            new DateTimeOffset(DateTime.SpecifyKind(membership.CreatedAtUtc, DateTimeKind.Utc)));
        return Created($"/api/tenants/{tenantId:D}/members", response);
    }

    [HttpPost]
    [ProducesResponseType<CreateTenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateTenantResponse>> Create(
        CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var userExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            return Unauthorized();
        }

        var slugExists = await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(tenant => tenant.Slug == request.Slug, cancellationToken);
        if (slugExists)
        {
            return SlugConflict();
        }

        var createdAtUtc = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = request.Slug,
            CreatedAtUtc = createdAtUtc
        };
        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = userId,
            Role = TenantRoles.Owner,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.Tenants.Add(tenant);
        dbContext.TenantMemberships.Add(membership);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTenantSlugUniqueViolation(exception))
        {
            return SlugConflict();
        }

        var response = new CreateTenantResponse(tenant.Id, tenant.Name, tenant.Slug, membership.Role);
        return Created($"/api/tenants/{tenant.Id:D}", response);
    }

    private ConflictObjectResult SlugConflict() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "A tenant with this slug already exists."
        });

    private ConflictObjectResult MembershipConflict() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The user is already a member of this tenant."
        });

    private bool TryGetAuthenticatedUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private static bool IsTenantSlugUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Tenants_Slug"
        };

    private static bool IsTenantMembershipUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_TenantMemberships_TenantId_UserId"
        };
}
