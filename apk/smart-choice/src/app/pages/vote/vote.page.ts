import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { combineLatest, firstValueFrom, Subscription } from 'rxjs';
import { PollResultOptionDto } from '../../models/api.models';
import { ApiErrorService } from '../../services/api-error.service';
import { AuthApiService } from '../../services/auth-api.service';
import { AuthSessionService } from '../../services/auth-session.service';
import { PollsApiService } from '../../services/polls-api.service';

@Component({
  selector: 'app-vote',
  templateUrl: './vote.page.html',
  styleUrls: ['./vote.page.scss'],
  standalone: false,
})
export class VotePage implements OnInit, OnDestroy {
  readonly skeletonSlots: number[] = [1, 2, 3, 4];

  pollId = 0;
  inviteCode: string | null = null;

  isAuthLoading = false;
  authError: string | null = null;

  isLoading = true;
  loadError: string | null = null;
  options: PollResultOptionDto[] = [];

  selectedPhotoId: number | null = null;
  isSubmittingVote = false;
  voteError: string | null = null;

  private routeSubscription?: Subscription;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly authApiService: AuthApiService,
    public readonly authSessionService: AuthSessionService,
    private readonly pollsApiService: PollsApiService,
    private readonly apiErrorService: ApiErrorService
  ) {}

  ngOnInit(): void {
    this.routeSubscription = combineLatest([
      this.route.paramMap,
      this.route.queryParamMap,
    ]).subscribe(([params, query]) => {
      const pollId = Number(params.get('pollId'));
      const inviteCode = this.normalizeInviteCode(query.get('inviteCode'));
      void this.initializeView(pollId, inviteCode);
    });
  }

  ngOnDestroy(): void {
    this.routeSubscription?.unsubscribe();
  }

  get canConfirmVote(): boolean {
    return (
      !this.isSubmittingVote &&
      !this.isLoading &&
      !this.isAuthLoading &&
      !this.loadError &&
      this.options.length > 0 &&
      this.selectedPhotoId !== null &&
      this.authSessionService.hasToken()
    );
  }

  selectPhoto(photoId: number): void {
    if (this.isSubmittingVote || this.isLoading) {
      return;
    }

    this.selectedPhotoId = photoId;
    this.voteError = null;
  }

  async retryInvite(): Promise<void> {
    if (!this.inviteCode) {
      return;
    }

    await this.ensureInviteToken(this.inviteCode);
  }

  async retryLoadPoll(): Promise<void> {
    if (this.pollId <= 0) {
      return;
    }

    await this.loadPollOptions(this.pollId);
  }

  async confirmVote(): Promise<void> {
    if (!this.canConfirmVote || this.selectedPhotoId === null) {
      return;
    }

    this.isSubmittingVote = true;
    this.voteError = null;

    try {
      await firstValueFrom(
        this.pollsApiService.castVote({
          pollId: this.pollId,
          pollPhotoId: this.selectedPhotoId,
        })
      );

      void this.router.navigate(['/results', this.pollId]);
    } catch (error: unknown) {
      this.voteError = this.apiErrorService.toMessage(
        error,
        'Nie udało się oddać głosu. Spróbuj ponownie.'
      );
    } finally {
      this.isSubmittingVote = false;
    }
  }

  private async initializeView(pollId: number, inviteCode: string | null): Promise<void> {
    this.pollId = pollId;
    this.inviteCode = inviteCode;
    this.selectedPhotoId = null;
    this.voteError = null;
    this.authError = null;

    if (!Number.isInteger(pollId) || pollId <= 0) {
      this.isLoading = false;
      this.loadError = 'Link głosowania ma niepoprawny pollId.';
      this.options = [];
      return;
    }

    if (inviteCode) {
      await this.ensureInviteToken(inviteCode);
    }

    await this.loadPollOptions(pollId);
  }

  private async ensureInviteToken(inviteCode: string): Promise<void> {
    this.isAuthLoading = true;
    this.authError = null;

    try {
      const tokenResponse = await firstValueFrom(this.authApiService.issueGuestToken(inviteCode));
      this.authSessionService.setGuestSession(tokenResponse);
    } catch (error: unknown) {
      this.authError = this.apiErrorService.toMessage(
        error,
        'Nie udało się potwierdzić zaproszenia.'
      );
    } finally {
      this.isAuthLoading = false;
    }
  }

  private async loadPollOptions(pollId: number): Promise<void> {
    this.isLoading = true;
    this.loadError = null;

    try {
      const results = await firstValueFrom(this.pollsApiService.getResults(pollId));
      this.options = [...results.options].sort((a, b) => a.displayOrder - b.displayOrder);
    } catch (error: unknown) {
      this.loadError = this.apiErrorService.toMessage(
        error,
        'Nie udało się pobrać zdjęć do głosowania.'
      );
      this.options = [];
    } finally {
      this.isLoading = false;
    }
  }

  private normalizeInviteCode(inviteCode: string | null): string | null {
    if (!inviteCode) {
      return null;
    }

    const normalized = inviteCode.trim().toUpperCase();
    return normalized.length > 0 ? normalized : null;
  }
}
