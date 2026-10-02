import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { DevUserSwitcherComponent } from './core/dev-user-switcher.component';

@Component({
  imports: [RouterOutlet, RouterLink, DevUserSwitcherComponent],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {}
