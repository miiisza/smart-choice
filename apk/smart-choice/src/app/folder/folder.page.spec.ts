import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterModule } from '@angular/router';
import { IonicModule } from '@ionic/angular';

import { FolderPage } from './folder.page';

describe('FolderPage', () => {
  let component: FolderPage;
  let fixture: ComponentFixture<FolderPage>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [FolderPage],
      imports: [IonicModule.forRoot(), RouterModule.forRoot([]), FormsModule, HttpClientTestingModule]
    }).compileComponents();

    fixture = TestBed.createComponent(FolderPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
