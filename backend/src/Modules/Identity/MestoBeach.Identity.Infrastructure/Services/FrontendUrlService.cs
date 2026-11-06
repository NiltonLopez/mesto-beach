using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace MestoBeach.Identity.Infrastructure.Services;

public sealed class FrontendUrlService : IFrontendUrlService
{
    private readonly FrontendOptions _opts;

    public FrontendUrlService(IOptions<FrontendOptions> opts) => _opts = opts.Value;

    public string AdminImpersonateUrl(string token) =>
        $"{_opts.AdminUrl}/impersonate?token={token}";

    public string StaffActivationUrl(string activationToken) =>
        $"{_opts.AdminUrl}/staff/activate?token={activationToken}";

    public string StaffPasswordResetUrl(string resetToken) =>
        $"{_opts.AdminUrl}/staff/password-reset?token={resetToken}";

    public string CustomerPasswordResetUrl(string resetToken) =>
        $"{_opts.BaseUrl}/reset-password?token={resetToken}";
}
