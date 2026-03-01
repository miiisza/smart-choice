import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom, Subscription } from 'rxjs';
import { PollResultOptionDto, PollResultsDto } from '../../models/api.models';
import { ApiErrorService } from '../../services/api-error.service';
import { PollsApiService } from '../../services/polls-api.service';

@Component({
  selector: 'app-results',
  templateUrl: './results.page.html',
  styleUrls: ['./results.page.scss'],
  standalone: false,
})
export class ResultsPage implements OnInit, OnDestroy {
  readonly skeletonSlots: number[] = [1, 2, 3, 4];

  pollId = 0;
  isLoading = true;
  errorMessage: string | null = null;
  results: PollResultsDto | null = null;

  private routeSubscription?: Subscription;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly pollsApiService: PollsApiService,
    private readonly apiErrorService: ApiErrorService
  ) {}

  ngOnInit(): void {
    this.routeSubscription = this.route.paramMap.subscribe((params) => {
      const pollId = Number(params.get('pollId'));
      void this.loadResults(pollId);
    });
  }

  ngOnDestroy(): void {
    this.routeSubscription?.unsubscribe();
  }

  get options(): PollResultOptionDto[] {
    if (!this.results) {
      return [];
    }

    return [...this.results.options].sort((a, b) => a.displayOrder - b.displayOrder);
  }

  retry(): void {
    void this.loadResults(this.pollId);
  }

  openVote(): void {
    if (this.pollId <= 0) {
      return;
    }

    void this.router.navigate(['/vote', this.pollId]);
  }

  private async loadResults(pollId: number): Promise<void> {
    this.pollId = pollId;
    this.isLoading = true;
    this.errorMessage = null;

    if (!Number.isInteger(pollId) || pollId <= 0) {
      this.isLoading = false;
      this.results = null;
      this.errorMessage = 'Niepoprawne pollId w adresie.';
      return;
    }

    try {
      this.results = await firstValueFrom(this.pollsApiService.getResults(pollId));
    } catch (error: unknown) {
      this.results = null;
      this.errorMessage = this.apiErrorService.toMessage(
        error,
        'Nie udało się pobrać wyników.'
      );
    } finally {
      this.isLoading = false;
    }
  }
}
