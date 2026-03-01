import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { InviteRedirectPage } from './invite-redirect.page';

const routes: Routes = [
  {
    path: '',
    component: InviteRedirectPage,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class InviteRedirectPageRoutingModule {}
