
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:provider/provider.dart';

import 'core/theme/app_theme.dart';
import 'core/constants/api_constants.dart';
import 'features/navigation/main_navigation_page.dart';
import 'providers/auth_provider.dart';
import 'providers/leaderboard_provider.dart';

void main() {
  if (kReleaseMode && !ApiConstants.baseUrl.startsWith('https://')) {
    throw StateError('Release builds require an HTTPS API_BASE_URL.');
  }
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
          create: (_) => AuthProvider(),
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
