
import 'package:flutter/material.dart';

import 'core/theme/app_theme.dart';
import 'features/home/screens/home_page.dart';
import 'features/puzzle/screens/puzzle_page.dart';
import 'features/leaderboard/screens/leaderboard_page.dart';
import 'features/profile/screens/profile_page.dart';

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

class MainNavigationPage extends StatefulWidget {
  const MainNavigationPage({super.key});

  @override
  State<MainNavigationPage> createState() => _MainNavigationPageState();
}

class _MainNavigationPageState extends State<MainNavigationPage> {
  int _selectedIndex = 0;

  // Ana sayfadan diğer sekmelere geçiş için callback'ler.
  late final List<Widget> _pages = [
    HomePage(
      onOpenPuzzle: () => _navigateTo(1),
      onOpenLeaderboard: () => _navigateTo(2),
    ),
    const PuzzlePage(),
    const LeaderboardPage(),
    const ProfilePage(),
  ];

  void _navigateTo(int index) {
    setState(() {
      _selectedIndex = index;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(
        index: _selectedIndex,
        children: _pages,
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _selectedIndex,
        onDestinationSelected: _navigateTo,
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home),
            label: 'Ana Sayfa',
          ),
          NavigationDestination(
            icon: Icon(Icons.extension_outlined),
            selectedIcon: Icon(Icons.extension),
            label: 'Bulmaca',
          ),
          NavigationDestination(
            icon: Icon(Icons.leaderboard_outlined),
            selectedIcon: Icon(Icons.leaderboard),
            label: 'Sıralama',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profil',
          ),
        ],
      ),
    );
  }
}
