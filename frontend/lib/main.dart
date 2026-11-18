
import 'package:flutter/material.dart';

import 'core/theme/app_theme.dart';
import 'features/navigation/main_navigation_page.dart';

void main() {
  runApp(const DailyChessPuzzleApp());
}

class DailyChessPuzzleApp extends StatelessWidget {
  const DailyChessPuzzleApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Daily Chess Puzzle',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.darkTheme,
      home: const MainNavigationPage(),
    );
  }
}
