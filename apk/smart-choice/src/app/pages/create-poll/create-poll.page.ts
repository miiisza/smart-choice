import { Component, OnDestroy } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { filter, take, tap } from 'rxjs/operators';
import { PollPhotoDto } from '../../models/api.models';
import { ApiErrorService } from '../../services/api-error.service';
import { AuthApiService } from '../../services/auth-api.service';
import { AuthSessionService } from '../../services/auth-session.service';
import { PollPhotoUploadService, UploadState } from '../../services/poll-photo-upload.service';
import { PollsApiService } from '../../services/polls-api.service';

type WizardStep = 1 | 2 | 3;
type UploadVisualStatus = 'queued' | 'uploading' | 'done' | 'error';

interface SelectedPhoto {
  id: string;
  file: File;
  previewUrl: string;
  uploadStatus: UploadVisualStatus;
  progress: number;
  attempt: number;
  errorMessage?: string;
  uploadedPhoto?: PollPhotoDto;
}

@Component({
  selector: 'app-create-poll',
  templateUrl: './create-poll.page.html',
  styleUrls: ['./create-poll.page.scss'],
  standalone: false,
})
export class CreatePollPage implements OnDestroy {
  readonly minPhotos = 2;
  readonly maxPhotos = 4;
  readonly skeletonSlots: number[] = [1, 2, 3, 4];

  step: WizardStep = 1;
  selectedPhotos: SelectedPhoto[] = [];

  question = '';
  latitude = '52.2297';
  longitude = '21.0122';
  radiusMeters = '5000';

  authorEmail = 'alice@smartchoice.local';
  authorPassword = 'Alice123!';
  inviteCode = 'DEV2026';

  isLoggingIn = false;
  loginError: string | null = null;

  isPublishing = false;
  isPublished = false;
  publishError: string | null = null;
  createdPollId: number | null = null;

  constructor(
    private readonly router: Router,
    public readonly authSessionService: AuthSessionService,
    private readonly authApiService: AuthApiService,
    private readonly pollsApiService: PollsApiService,
    private readonly pollPhotoUploadService: PollPhotoUploadService,
    private readonly apiErrorService: ApiErrorService
  ) {}

  ngOnDestroy(): void {
    for (const photo of this.selectedPhotos) {
      URL.revokeObjectURL(photo.previewUrl);
    }
  }

  get hasValidPhotoCount(): boolean {
    return this.selectedPhotos.length >= this.minPhotos && this.selectedPhotos.length <= this.maxPhotos;
  }

  get hasValidAudience(): boolean {
    return (
      this.question.trim().length >= 3 &&
      this.parseNumber(this.latitude) !== null &&
      this.parseNumber(this.longitude) !== null &&
      this.parseNumber(this.radiusMeters) !== null
    );
  }

  get canPublish(): boolean {
    return this.hasValidPhotoCount && this.hasValidAudience && this.authSessionService.isUserSession();
  }

  get publishedPhotosCount(): number {
    return this.selectedPhotos.filter((photo) => photo.uploadStatus === 'done').length;
  }

  get publishProgress(): number {
    if (this.selectedPhotos.length === 0) {
      return 0;
    }

    return this.publishedPhotosCount / this.selectedPhotos.length;
  }

  get normalizedInviteCode(): string {
    const normalized = this.inviteCode.trim().toUpperCase();
    return normalized.length > 0 ? normalized : 'DEV2026';
  }

  get webInviteLink(): string {
    if (!this.createdPollId) {
      return '';
    }

    return `${window.location.origin}/invite/${encodeURIComponent(this.normalizedInviteCode)}?pollId=${this.createdPollId}`;
  }

  get capacitorInviteLink(): string {
    if (!this.createdPollId) {
      return '';
    }

    return `smartchoice://invite/${encodeURIComponent(this.normalizedInviteCode)}?pollId=${this.createdPollId}`;
  }

  goToStep(nextStep: WizardStep): void {
    if (nextStep === 2 && !this.hasValidPhotoCount) {
      return;
    }

    if (nextStep === 3 && !this.hasValidAudience) {
      return;
    }

    this.step = nextStep;
  }

  onPhotoSelectionChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (files.length === 0) {
      return;
    }

    for (const file of files) {
      if (this.selectedPhotos.length >= this.maxPhotos) {
        break;
      }

      this.selectedPhotos.push({
        id: `${Date.now()}-${Math.round(Math.random() * 100000)}`,
        file,
        previewUrl: URL.createObjectURL(file),
        uploadStatus: 'queued',
        progress: 0,
        attempt: 0,
      });
    }

    this.publishError = null;
    input.value = '';
  }

  removePhoto(photoId: string): void {
    const photoToRemove = this.selectedPhotos.find((item) => item.id === photoId);
    if (photoToRemove) {
      URL.revokeObjectURL(photoToRemove.previewUrl);
    }

    this.selectedPhotos = this.selectedPhotos.filter((item) => item.id !== photoId);
  }

  async signInAsAuthor(): Promise<void> {
    if (this.isLoggingIn) {
      return;
    }

    this.loginError = null;
    this.isLoggingIn = true;

    try {
      const tokenResponse = await firstValueFrom(
        this.authApiService.login(this.authorEmail.trim(), this.authorPassword)
      );

      this.authSessionService.setUserSession(tokenResponse);
    } catch (error: unknown) {
      this.loginError = this.apiErrorService.toMessage(
        error,
        'Nie udało się zalogować konta autora.'
      );
    } finally {
      this.isLoggingIn = false;
    }
  }

  logout(): void {
    this.authSessionService.clear();
  }

  async publish(): Promise<void> {
    if (this.isPublishing) {
      return;
    }

    if (!this.canPublish) {
      this.publishError = 'Uzupełnij zdjęcia, audiencję i zaloguj konto autora.';
      return;
    }

    this.isPublishing = true;
    this.isPublished = false;
    this.publishError = null;
    this.createdPollId = null;

    this.resetUploadStatuses();

    try {
      const draft = await firstValueFrom(
        this.pollsApiService.createPollDraft({
          question: this.question.trim(),
          photoUrls: [],
          latitude: this.parseNumber(this.latitude) as number,
          longitude: this.parseNumber(this.longitude) as number,
          radiusMeters: Math.round(this.parseNumber(this.radiusMeters) as number),
        })
      );

      this.createdPollId = draft.id;

      for (const photo of this.selectedPhotos) {
        const uploadResult = await firstValueFrom(
          this.pollPhotoUploadService.uploadWithRetry(draft.id, photo.file, 2).pipe(
            tap((state) => this.applyUploadState(photo, state)),
            filter((state) => state.status === 'done' || state.status === 'error'),
            take(1)
          )
        );

        if (uploadResult.status !== 'done' || !uploadResult.photo) {
          throw new Error(
            uploadResult.errorMessage ?? `Upload zdjęcia "${photo.file.name}" zakończył się błędem.`
          );
        }
      }

      await firstValueFrom(this.pollsApiService.publishPoll(draft.id));
      this.isPublished = true;
    } catch (error: unknown) {
      this.publishError = this.apiErrorService.toMessage(
        error,
        'Nie udało się opublikować ankiety. Spróbuj ponownie.'
      );
    } finally {
      this.isPublishing = false;
    }
  }

  openVoteScreen(): void {
    if (!this.createdPollId) {
      return;
    }

    void this.router.navigate(['/vote', this.createdPollId], {
      queryParams: {
        inviteCode: this.normalizedInviteCode,
      },
    });
  }

  openResultsScreen(): void {
    if (!this.createdPollId) {
      return;
    }

    void this.router.navigate(['/results', this.createdPollId]);
  }

  photoStatusLabel(photo: SelectedPhoto): string {
    switch (photo.uploadStatus) {
      case 'queued':
        return 'W kolejce';
      case 'uploading':
        return `Upload ${photo.progress}%`;
      case 'done':
        return 'Wysłane';
      case 'error':
        return 'Błąd';
      default:
        return 'Nieznany status';
    }
  }

  private resetUploadStatuses(): void {
    this.selectedPhotos = this.selectedPhotos.map((photo) => ({
      ...photo,
      uploadStatus: 'queued',
      progress: 0,
      attempt: 0,
      errorMessage: undefined,
      uploadedPhoto: undefined,
    }));
  }

  private applyUploadState(photo: SelectedPhoto, state: UploadState): void {
    photo.attempt = state.attempt;
    photo.progress = state.progress;

    if (state.status === 'in-progress') {
      photo.uploadStatus = 'uploading';
      return;
    }

    if (state.status === 'done') {
      photo.uploadStatus = 'done';
      photo.uploadedPhoto = state.photo;
      photo.errorMessage = undefined;
      return;
    }

    if (state.status === 'error') {
      photo.uploadStatus = 'error';
      photo.errorMessage = state.errorMessage;
      return;
    }

    photo.uploadStatus = 'queued';
  }

  private parseNumber(value: string): number | null {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
}
