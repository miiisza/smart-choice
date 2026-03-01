import { NgModule } from '@angular/core';
import { PreloadAllModules, RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    redirectTo: 'create',
    pathMatch: 'full',
  },
  {
    path: 'create',
    loadChildren: () =>
      import('./pages/create-poll/create-poll.module').then((m) => m.CreatePollPageModule),
  },
  {
    path: 'vote/:pollId',
    loadChildren: () => import('./pages/vote/vote.module').then((m) => m.VotePageModule),
  },
  {
    path: 'results/:pollId',
    loadChildren: () =>
      import('./pages/results/results.module').then((m) => m.ResultsPageModule),
  },
  {
    path: 'invite/:inviteCode',
    loadChildren: () =>
      import('./pages/invite-redirect/invite-redirect.module').then((m) => m.InviteRedirectPageModule),
  },
  {
    path: '**',
    redirectTo: 'create',
  },
];

@NgModule({
  imports: [RouterModule.forRoot(routes, { preloadingStrategy: PreloadAllModules })],
  exports: [RouterModule],
})
export class AppRoutingModule {}
