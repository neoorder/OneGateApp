using Android.App;
using Android.Content;
using Firebase.Messaging;
using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate.Platforms.Android;

[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class OneGateFirebaseMessagingService : FirebaseMessagingService
{
    public override void OnRegistered(string installationId)
    {
        base.OnRegistered(installationId);
        IPlatformApplication.Current!.Services.GetRequiredService<PushRegistrationService>()
            .OnFirebaseRegistered(installationId);
    }
}
