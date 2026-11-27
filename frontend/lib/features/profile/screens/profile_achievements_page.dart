import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/profile_provider.dart';

class ProfileAchievementsPage extends StatefulWidget {
  const ProfileAchievementsPage({super.key});
  @override
  State<ProfileAchievementsPage> createState() =>
      _ProfileAchievementsPageState();
}

class _ProfileAchievementsPageState extends State<ProfileAchievementsPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback(
      (_) => context.read<ProfileProvider>().refresh(),
    );
  }

  @override
  Widget build(BuildContext context) {
    final provider = context.watch<ProfileProvider>();
    final achievements =
        (provider.summary?['achievements'] as List?) ?? const [];
    return Scaffold(
      appBar: AppBar(title: const Text('Başarımlar')),
      body: provider.loading && achievements.isEmpty
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (provider.error != null)
                  Text(
                    provider.error!,
                    style: const TextStyle(color: Colors.redAccent),
                  ),
                ...achievements.map((raw) {
                  final a = Map<String, dynamic>.from(raw as Map);
                  final unlocked = a['unlocked'] == true;
                  final progress = (a['progress'] as num?)?.toInt() ?? 0;
                  final goal = (a['goal'] as num?)?.toInt() ?? 1;
                  return Card(
                    color: const Color(0xFF111111),
                    child: ListTile(
                      leading: Icon(
                        unlocked ? Icons.workspace_premium : Icons.lock_outline,
                        color: unlocked
                            ? const Color(0xFFD4AF37)
                            : Colors.white38,
                      ),
                      title: Text(
                        a['title']?.toString() ?? '',
                        style: TextStyle(
                          color: unlocked
                              ? const Color(0xFFE8C85A)
                              : Colors.white70,
                        ),
                      ),
                      subtitle: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            a['description']?.toString() ?? '',
                            style: const TextStyle(color: Colors.white60),
                          ),
                          const SizedBox(height: 8),
                          LinearProgressIndicator(
                            value: (progress / goal).clamp(0.0, 1.0).toDouble(),
                            color: const Color(0xFFD4AF37),
                            backgroundColor: Colors.white12,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            '$progress / $goal',
                            style: const TextStyle(color: Colors.white54),
                          ),
                        ],
                      ),
                    ),
                  );
                }),
              ],
            ),
    );
  }
}
