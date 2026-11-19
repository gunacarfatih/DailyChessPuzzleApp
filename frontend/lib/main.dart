
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/theme/app_theme.dart';
import 'features/navigation/main_navigation_page.dart';
import 'providers/auth_provider.dart';
import 'providers/leaderboard_provider.dart';

void main() {
  runApp(const DailyChessPuzzleApp());
}

class DailyChessPuzzleApp extends StatelessWidget {
  const DailyChessPuzzleApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        // Kullanıcı oturumunu yönetir.
        ChangeNotifierProvider(
          create: (_) => AuthProvider()..initialize(),
        ),

        // Liderlik tablosu verilerini yönetir.
        ChangeNotifierProvider(
          create: (_) => LeaderboardProvider(),
        ),
      ],
      child: MaterialApp(
        title: 'Daily Chess Puzzle',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.darkTheme,
        home: const MainNavigationPage(),
      ),
    );
  }
}
