using Meshline.Models;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Services.Files;

namespace NeoOrder.OneGate.Pages;

public partial class ChatSettingsPage
{
    bool choosingAvatar;
    (byte[] Bytes, string FileName, FileUploadOperation Operation)? avatarDraft;
    ContentReference? uploadedAvatar;
    ChatAvatarCropPage? avatarCrop;
    bool HasAvatarEdits => avatarDraft is not null || HasChange(profileDraft.Avatar);
    public bool CanEditAvatar => CanEdit;

    async void OnEditAvatarClicked(object sender, EventArgs e) => await EditAvatarAsync();

    async Task EditAvatarAsync()
    {
        if (!CanEditAvatar || localAccountId is null) return;
        choosingAvatar = true;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        BusyText = null;
        NotifyState();
        ProfileUpdate? update = null;
        bool preparingPhoto = false;
        try
        {
            string choice = await DisplayActionSheetAsync(Strings.ChatAvatar, Strings.Cancel,
                messagingService.ChatAvatar is not null || HasChange(profileUpdateState?.PendingUpdate?.Avatar ?? default)
                    || HasChange(profileDraft.Avatar) || uploadedAvatar is not null
                    ? Strings.ChatAvatarRemove : null, Strings.ChatAvatarChoose);
            cancellation.Token.ThrowIfCancellationRequested();
            if (choice == Strings.ChatAvatarRemove)
            {
                avatarDraft = null;
                uploadedAvatar = null;
                update = new() { Avatar = FieldUpdate<ContentReference>.Delete };
            }
            else if (choice == Strings.ChatAvatarChoose)
            {
                preparingPhoto = true;
                FileResult? selected;
                if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
                    selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Strings.ChatAvatarChoose, FileTypes = FilePickerFileType.Images });
                else
                    selected = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = Strings.ChatAvatarChoose, SelectionLimit = 1 })).FirstOrDefault();
                cancellation.Token.ThrowIfCancellationRequested();
                if (selected is null) return;
                BusyText = Strings.Loading;
                await using var stream = await selected.OpenReadAsync();
                byte[] bytes = await ChatAvatarImages.ReadBoundedAsync(stream, 20 * 1024 * 1024, cancellation.Token);
                var photo = await Task.Run(() => ChatAvatarImages.PreparePhoto(bytes), cancellation.Token);
                if (cancellation.IsCancellationRequested) { photo.Dispose(); return; }
                avatarCrop = new ChatAvatarCropPage(photo);
                using var canceled = cancellation.Token.Register(() => MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (avatarCrop is { } crop) await crop.CloseAsync(null);
                }));
                BusyText = null;
                await Navigation.PushModalAsync(avatarCrop);
                byte[]? cropped = await avatarCrop.Result;
                if (cropped is null) return;
                cancellation.Token.ThrowIfCancellationRequested();
                preparingPhoto = false;
                BusyText = Strings.Loading;
                // Picking a photo can suspend the SDK session. Restore it before touching pending work.
                await messagingService.InitializeAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                // Keep an already submitted publication until the new avatar reaches the SDK.
                if (!IsCurrentAccount) { InvalidateAccount(); return; }
                avatarDraft = (cropped, selected.FileName, FileUploadOperation.Start(DateTimeOffset.UtcNow));
                avatarCrop = null;
                uploadedAvatar = null;
            }
            else return;
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception)
        {
            if (!accountChanged) ErrorText = preparingPhoto ? Strings.ChatAvatarInvalidImage : Strings.ChatSettingsSaveFailed;
            return;
        }
        finally
        {
            avatarCrop = null;
            choosingAvatar = false;
            await FinishOperationAsync(cancellation);
        }
        if (!CanEdit || cancellation.IsCancellationRequested) return;
        if (update is not null) await SaveAsync(update);
        else if (avatarDraft is not null) await UploadAvatarDraftAsync();
    }

    async Task UploadAvatarDraftAsync()
    {
        if (!CanEdit || localAccountId is null || avatarDraft is not { } draft) return;
        if (uploadedAvatar is null && draft.Operation.IsExpired(DateTimeOffset.UtcNow))
        {
            avatarDraft = null;
            uploadedAvatar = null;
            ErrorText = Strings.ChatAvatarUploadExpired;
            NotifyState();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        BusyText = Strings.ChatSettingsSaving;
        ErrorText = null;
        NotifyState();
        try
        {
            uploadedAvatar ??= await messagingService.UploadChatAvatarAsync(localAccountId, draft.Operation.Id, draft.FileName, draft.Bytes, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentAccount) { InvalidateAccount(); return; }
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { if (!accountChanged) ErrorText = Strings.ChatAvatarSaveFailed; }
        finally { await FinishOperationAsync(cancellation); }
        if (CanEdit && !cancellation.IsCancellationRequested && uploadedAvatar is not null)
            await SaveAsync(new ProfileUpdate { Avatar = uploadedAvatar });
    }

    void ClearAvatarDraft()
    {
        avatarDraft = null;
        uploadedAvatar = null;
    }
}
