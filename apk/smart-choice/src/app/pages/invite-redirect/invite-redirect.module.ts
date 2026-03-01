import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IonicModule } from '@ionic/angular';
import { InviteRedirectPageRoutingModule } from './invite-redirect-routing.module';
import { InviteRedirectPage } from './invite-redirect.page';

@NgModule({
  imports: [CommonModule, FormsModule, IonicModule, InviteRedirectPageRoutingModule],
  declarations: [InviteRedirectPage],
})
export class InviteRedirectPageModule {}
