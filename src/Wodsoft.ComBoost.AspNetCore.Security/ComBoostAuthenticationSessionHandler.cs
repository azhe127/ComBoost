﻿using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;

namespace Wodsoft.ComBoost.Security
{
    public class ComBoostAuthenticationSessionHandler : AuthenticationHandler<ComBoostAuthenticationOptions>
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var ticketData = Context.Session.GetString(Options.CookieName(Context));
            if (ticketData == null)
                return Task.FromResult(AuthenticateResult.Skip());
            try
            {
                var ticket = Options.TicketDataFormat.Unprotect(ticketData, GetTlsTokenBinding());
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AuthenticateResult.Fail(ex));
            }
        }

        protected override Task HandleSignInAsync(SignInContext context)
        {
            var ticket = new AuthenticationTicket(context.Principal, null, Options.AuthenticationScheme);
            var ticketValue = Options.TicketDataFormat.Protect(ticket, GetTlsTokenBinding());
            Context.Session.SetString(Options.CookieName(Context), ticketValue);
#if NET451
            return Task.FromResult(0);
#else
            return Task.CompletedTask;
#endif
        }

        protected override Task HandleSignOutAsync(SignOutContext context)
        {
            Context.Session.Remove(Options.CookieName(Context));
#if NET451
            return Task.FromResult(0);
#else
            return Task.CompletedTask;
#endif
        }

        protected virtual string GetTlsTokenBinding()
        {
            var binding = Context.Features.Get<ITlsTokenBindingFeature>()?.GetProvidedTokenBindingId();
            return binding == null ? null : Convert.ToBase64String(binding);
        }
    }
}
