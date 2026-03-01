import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ResultsPage } from './results.page';

const routes: Routes = [
  {
    path: '',
    component: ResultsPage,
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ResultsPageRoutingModule {}
