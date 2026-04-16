#[compute]
#version 450

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct ActorData {
	int x;
	int y;
	int z;
	uint faction;
	int facingX;
	int facingY;
	float sightCapacity;
	uint flags;
};

struct ObserverData {
	uint actorIndex;
	int frontRadius;
	int rearRadius;
	int maxVerticalLayers;
};

struct VisionResult {
	uint observerIdx;
	uint visibleCount;
	uint visibleIndices[12];
};

layout(set = 0, binding = 0, std430) restrict readonly buffer ActorBuffer {
	ActorData actors[];
};

layout(set = 0, binding = 1, std430) restrict readonly buffer ObserverBuffer {
	ObserverData observers[];
};

layout(set = 0, binding = 2, std430) restrict buffer ResultBuffer {
	VisionResult results[];
};

layout(set = 0, binding = 3) uniform sampler3D terrainOpacity;

layout(push_constant, std430) uniform PushConstants {
	uint actorCount;
	uint observerCount;
	uint shortlistLimit;
	int worldMinX;
	int worldMinY;
	int worldMinZ;
	int worldSizeX;
	int worldSizeY;
	int worldSizeZ;
};

bool isHostile(uint factionA, uint factionB) {
	if (factionA == factionB) return false;
	if (factionA == 1u && (factionB == 0u || factionB == 2u)) return true;
	if (factionB == 1u && (factionA == 0u || factionA == 2u)) return true;
	return false;
}

bool blocksSight3D(int x, int y, int z) {
	int lx = x - worldMinX;
	int ly = y - worldMinY;
	int lz = z - worldMinZ;
	if (lx < 0 || ly < 0 || lz < 0 || lx >= worldSizeX || ly >= worldSizeY || lz >= worldSizeZ)
		return true;
	float opacity = texelFetch(terrainOpacity, ivec3(lx, ly, lz), 0).r;
	return opacity > 0.5;
}

bool hasLineOfSight3D(int ox, int oy, int oz, int tx, int ty, int tz) {
	if (ox == tx && oy == ty && oz == tz) return true;

	int dx = tx - ox;
	int dy = ty - oy;
	int dz = tz - oz;
	int nx = abs(dx);
	int ny = abs(dy);
	int nz = abs(dz);
	int sx = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
	int sy = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
	int sz = dz > 0 ? 1 : (dz < 0 ? -1 : 0);

	int x = ox, y = oy, z = oz;
	float tMaxX = nx > 0 ? 0.5 / float(nx) : 1e30;
	float tMaxY = ny > 0 ? 0.5 / float(ny) : 1e30;
	float tMaxZ = nz > 0 ? 0.5 / float(nz) : 1e30;
	float tDeltaX = nx > 0 ? 1.0 / float(nx) : 1e30;
	float tDeltaY = ny > 0 ? 1.0 / float(ny) : 1e30;
	float tDeltaZ = nz > 0 ? 1.0 / float(nz) : 1e30;

	int steps = nx + ny + nz;
	for (int i = 0; i < steps; i++) {
		if (tMaxX <= tMaxY && tMaxX <= tMaxZ) {
			x += sx;
			tMaxX += tDeltaX;
		} else if (tMaxY <= tMaxX && tMaxY <= tMaxZ) {
			y += sy;
			tMaxY += tDeltaY;
		} else {
			z += sz;
			tMaxZ += tDeltaZ;
		}

		if (x == tx && y == ty && z == tz)
			return true;

		if (blocksSight3D(x, y, z))
			return false;
	}

	return true;
}

struct Candidate {
	uint actorIdx;
	int score;
};

shared Candidate sharedCandidates[64];
shared uint sharedCandidateCount;

void main() {
	uint observerIdx = gl_WorkGroupID.x;
	uint threadIdx = gl_LocalInvocationID.x;

	if (observerIdx >= observerCount) return;

	ObserverData obs = observers[observerIdx];
	ActorData self = actors[obs.actorIndex];

	if (threadIdx == 0u) {
		sharedCandidateCount = 0u;
	}
	barrier();

	float frontSq = float(obs.frontRadius) * float(obs.frontRadius);
	float rearSq = float(obs.rearRadius) * float(obs.rearRadius);

	uint chunkSize = (actorCount + 63u) / 64u;
	uint start = threadIdx * chunkSize;
	uint end = min(start + chunkSize, actorCount);

	for (uint i = start; i < end; i++) {
		if (i == obs.actorIndex) continue;
		ActorData target = actors[i];
		if ((target.flags & 1u) != 0u) continue;

		int dx = target.x - self.x;
		int dy = target.y - self.y;
		int dz = target.z - self.z;
		if (abs(dz) > obs.maxVerticalLayers) continue;

		float distSq = float(dx)*float(dx) + float(dy)*float(dy) + float(dz)*float(dz);
		int dot = dx * self.facingX + dy * self.facingY;
		float maxSq = dot >= 0 ? frontSq : rearSq;
		if (distSq > maxSq) continue;

		int hostilityPenalty = isHostile(self.faction, target.faction) ? 0 : 10000;
		int rearPenalty = dot < 0 ? 2000 : 0;
		int crossLayerPenalty = dz != 0 ? 1000 : 0;
		int score = hostilityPenalty + rearPenalty + crossLayerPenalty + int(min(distSq, 500000000.0));

		uint slot = atomicAdd(sharedCandidateCount, 1u);
		if (slot < 64u) {
			sharedCandidates[slot].actorIdx = i;
			sharedCandidates[slot].score = score;
		}
	}

	barrier();

	uint totalCandidates = min(sharedCandidateCount, 64u);
	uint limit = min(shortlistLimit, totalCandidates);

	if (threadIdx == 0u) {
		for (uint i = 0u; i < limit; i++) {
			uint bestIdx = i;
			int bestScore = sharedCandidates[i].score;
			for (uint j = i + 1u; j < totalCandidates; j++) {
				if (sharedCandidates[j].score < bestScore) {
					bestScore = sharedCandidates[j].score;
					bestIdx = j;
				}
			}
			if (bestIdx != i) {
				Candidate tmp = sharedCandidates[i];
				sharedCandidates[i] = sharedCandidates[bestIdx];
				sharedCandidates[bestIdx] = tmp;
			}
		}

		uint visCount = 0u;
		for (uint i = 0u; i < limit && visCount < 12u; i++) {
			ActorData target = actors[sharedCandidates[i].actorIdx];
			if (hasLineOfSight3D(self.x, self.y, self.z, target.x, target.y, target.z)) {
				results[observerIdx].visibleIndices[visCount] = sharedCandidates[i].actorIdx;
				visCount++;
			}
		}
		results[observerIdx].observerIdx = obs.actorIndex;
		results[observerIdx].visibleCount = visCount;
	}
}
