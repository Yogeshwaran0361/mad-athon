using System.Net;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Notifications;

namespace ReworkGuard.Application.Notifications;

/// <summary>Turns a notification into a plain-text and HTML email with absolute links back into the app.</summary>
public static class EmailRenderer
{
    public static EmailMessage Render(Notification notification, string toAddress, string toName, string appBaseUrl)
    {
        var baseUrl = appBaseUrl.TrimEnd('/');
        var link = notification.Link is null ? baseUrl : baseUrl + notification.Link;
        var preferences = baseUrl + "/notifications/preferences";
        var footer = NotificationCatalog.IsEmailMandatory(notification.Type)
            ? "This is a formal notice and is always sent by email."
            : $"You can turn these emails off at {preferences}.";

        var text = $"Hello {toName},\n\n{notification.Body}\n\nOpen in ReworkGuard: {link}\n\n{footer}\n";
        var html = $"""
            <!doctype html>
            <html lang="en"><body style="font-family:Arial,Helvetica,sans-serif;color:#1f2933;line-height:1.5">
            <p>Hello {Encode(toName)},</p>
            <p>{Encode(notification.Body)}</p>
            <p><a href="{Encode(link)}">Open in ReworkGuard</a></p>
            <p style="color:#616e7c;font-size:12px">{Encode(footer)}</p>
            </body></html>
            """;
        return new EmailMessage(toAddress, toName, "[ReworkGuard] " + notification.Title, text, html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
