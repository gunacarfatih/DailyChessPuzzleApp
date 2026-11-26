import 'package:flutter/material.dart';

import '../../navigation/main_navigation_page.dart';
import '../../../providers/auth_provider.dart';

import 'package:provider/provider.dart';

class SplashPage extends StatefulWidget {
  const SplashPage({super.key});

  @override
  State<SplashPage> createState() => _SplashPageState();
}

class _SplashPageState extends State<SplashPage> {
  @override
  void initState() {
    super.initState();

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _initialize();
      }
    });
  }

  Future<void> _initialize() async {
    final authProvider = context.read<AuthProvider>();

    await Future.wait<void>([
      authProvider.initialize(),
      Future<void>.delayed(const Duration(milliseconds: 1800)),
    ]);

    if (!mounted) {
      return;
    }

    Navigator.of(context).pushReplacement(
      PageRouteBuilder<void>(
        transitionDuration: const Duration(milliseconds: 450),
        pageBuilder: (_, _, _) => const MainNavigationPage(),
        transitionsBuilder: (_, animation, _, child) {
          return FadeTransition(opacity: animation, child: child);
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF101A17),
      body: SizedBox.expand(
        child: Image.asset(
          'assets/branding/splash_screen.png',
          width: double.infinity,
          height: double.infinity,
          fit: BoxFit.cover,
          alignment: Alignment.center,
          semanticLabel: 'Checkrix - Think. Solve. Master.',
        ),
      ),
    );
  }
}
