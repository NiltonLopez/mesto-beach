using MestoBeach.Identity.Domain.Enums;
using FluentValidation;
using MestoBeach.Identity.Application.Auth.Staff.Commands;
using MestoBeach.Identity.Application.Auth.Customer.Commands;
using MestoBeach.Identity.Application.Auth.Platform.Commands;
using MestoBeach.Identity.Application.Staff.Commands;

namespace MestoBeach.Identity.Application.Common;

// ─── Staff Auth Validators ────────────────────────────────────────────────────

public sealed class LoginStaffValidator : AbstractValidator<LoginStaffCommand>
{
    public LoginStaffValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo es requerido.")
            .EmailAddress().WithMessage("Formato de correo inválido.")
            .MaximumLength(300);
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es requerida.");
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("El tenantId es requerido.");
    }
}

// ─── Customer Auth Validators ─────────────────────────────────────────────────

public sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomerCommand>
{
    private static readonly System.Text.RegularExpressions.Regex NameRegex =
        new(@"^[\p{L}\s\-]{2,200}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex PhoneRegex =
        new(@"^\+[1-9]\d{7,14}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public RegisterCustomerValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("La contraseña debe tener al menos 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("Debe contener al menos una letra mayúscula.")
            .Matches(@"[a-z]").WithMessage("Debe contener al menos una letra minúscula.")
            .Matches(@"[0-9]").WithMessage("Debe contener al menos un dígito.");
        RuleFor(x => x.Name)
            .NotEmpty()
            .Matches(NameRegex).WithMessage("El nombre solo puede contener letras, espacios y guiones (2–200 chars).");
        RuleFor(x => x.Phone)
            .Matches(PhoneRegex).WithMessage("Formato de teléfono inválido (E.164).")
            .When(x => x.Phone is not null);
        RuleFor(x => x.BirthDate)
            .Must(d => d!.Value <= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("La fecha de nacimiento no puede ser futura.")
            .Must(d => {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var age = today.Year - d!.Value.Year;
                if (d.Value.AddYears(age) > today) age--;
                return age >= 14;
            })
            .WithMessage("La edad mínima es 14 años.")
            .When(x => x.BirthDate.HasValue);
    }
}

// ─── Platform Validators ──────────────────────────────────────────────────────

public sealed class VerifyTotpValidator : AbstractValidator<VerifyTotpCommand>
{
    public VerifyTotpValidator()
    {
        RuleFor(x => x.TotpCode)
            .NotEmpty()
            .Matches(@"^\d{6}$").WithMessage("El código TOTP debe ser exactamente 6 dígitos.");
    }
}

public sealed class RecoverTotpValidator : AbstractValidator<RecoverTotpCommand>
{
    public RecoverTotpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.EmergencyCode)
            .NotEmpty()
            .Matches(@"^\d{24}$").WithMessage("El código de emergencia debe ser exactamente 24 dígitos.");
    }
}

// ─── Staff Management Validators ─────────────────────────────────────────────

public sealed class InviteStaffValidator : AbstractValidator<InviteStaffCommand>
{
    // Se deriva del enum en vez de mantener una lista a mano: esta lista se quedó atrás cuando
    // llegaron Host, PDA y Runner, y el resultado fue que invitar a un PDA devolvía 400 — el rol
    // existía en todo el sistema menos aquí. Derivándola, añadir un rol al enum basta.
    private static readonly string[] ValidRoles = Enum.GetNames<StaffRole>();
    private static readonly string[] ValidAccessModes = [StaffAccessMode.ActivationLink, StaffAccessMode.DirectAccess];

    public InviteStaffValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(300);
        RuleFor(x => x.Roles)
            .NotEmpty().WithMessage("Debe asignar al menos un rol.")
            .Must(rs => rs.All(r => ValidRoles.Contains(r)))
            .WithMessage($"Rol no válido. Valores permitidos: {string.Join(", ", ValidRoles)}.");
        RuleFor(x => x.AccessMode)
            .NotEmpty()
            .Must(m => ValidAccessModes.Contains(m))
            .WithMessage($"Modo de acceso no válido. Valores permitidos: {string.Join(", ", ValidAccessModes)}.");
    }
}

public sealed class ChangeStaffPasswordValidator : AbstractValidator<ChangeStaffPasswordCommand>
{
    public ChangeStaffPasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"[A-Z]").WithMessage("Debe contener al menos una letra mayúscula.")
            .Matches(@"[a-z]").WithMessage("Debe contener al menos una letra minúscula.")
            .Matches(@"[0-9]").WithMessage("Debe contener al menos un dígito.");
        RuleFor(x => x.ConfirmPassword).NotEmpty();
    }
}

public sealed class ActivateStaffValidator : AbstractValidator<ActivateStaffCommand>
{
    public ActivateStaffValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"[A-Z]").WithMessage("Debe contener al menos una letra mayúscula.")
            .Matches(@"[a-z]").WithMessage("Debe contener al menos una letra minúscula.")
            .Matches(@"[0-9]").WithMessage("Debe contener al menos un dígito.");
        RuleFor(x => x.ConfirmPassword).NotEmpty();
    }
}
