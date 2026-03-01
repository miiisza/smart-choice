import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { combineLatest, Subscription } from 'rxjs';

@Component({
  selector: 'app-invite-redirect',
  templateUrl: './invite-redirect.page.html',
  styleUrls: ['./invite-redirect.page.scss'],
  standalone: false,
})
export class InviteRedirectPage implements OnInit, OnDestroy {
  isRedirecting = true;
  errorMessage: string | null = null;

  private routeSubscription?: Subscription;

  constructor(private readonly route: ActivatedRoute, private readonly router: Router) {}

  ngOnInit(): void {
    this.routeSubscription = combineLatest([
      this.route.paramMap,
      this.route.queryParamMap,
    ]).subscribe(([params, query]) => {
      const inviteCode = params.get('inviteCode')?.trim().toUpperCase() ?? '';
      const pollId = Number(query.get('pollId') ?? query.get('poll'));

      if (!inviteCode || !Number.isInteger(pollId) || pollId <= 0) {
        this.isRedirecting = false;
        this.errorMessage = 'Link zaproszenia jest niepełny. Brakuje poprawnego pollId.';
        return;
      }

      void this.router.navigate(['/vote', pollId], {
        queryParams: {
          inviteCode,
        },
        replaceUrl: true,
      });
    });
  }

  ngOnDestroy(): void {
    this.routeSubscription?.unsubscribe();
  }
}
