import numpy as np, math
class Vector:
    def __init__(s, v): s.a = np.array(list(v), float)
    x = property(lambda s: s.a[0], lambda s, v: s._set(0, v)); y = property(lambda s: s.a[1], lambda s, v: s._set(1, v))
    z = property(lambda s: s.a[2], lambda s, v: s._set(2, v))
    def _set(s, i, v): s.a[i] = v
    def __iter__(s): return iter(s.a.tolist())
    def __len__(s): return len(s.a)
    def __getitem__(s, i): return s.a[i]
    def __add__(s, o): return Vector(s.a + _arr(o))
    __radd__ = __add__
    def __sub__(s, o): return Vector(s.a - _arr(o))
    def __mul__(s, k): return Vector(s.a * k)
    __rmul__ = __mul__
    def __truediv__(s, k): return Vector(s.a / k)
    def __neg__(s): return Vector(-s.a)
    def normalized(s):
        n = np.linalg.norm(s.a); return Vector(s.a / n if n else s.a)
    def copy(s): return Vector(s.a.copy())
    @property
    def length(s): return float(np.linalg.norm(s.a))
def _arr(o): return o.a if isinstance(o, Vector) else np.array(list(o), float)
class Matrix:
    def __init__(s, m): s.m = np.array(m, float)
    @staticmethod
    def Identity(n): return Matrix(np.eye(n))
    @staticmethod
    def Translation(v):
        m = np.eye(4); m[:3, 3] = list(v); return Matrix(m)
    @staticmethod
    def Diagonal(v): return Matrix(np.diag(list(v)))
    @staticmethod
    def Scale(k, n, axis=None): m = np.eye(n) * k; m[-1, -1] = 1 if n == 4 else k; return Matrix(m)
    @staticmethod
    def Rotation(a, n, axis):
        c, s_ = math.cos(a), math.sin(a)
        if axis == 'X': r = [[1, 0, 0], [0, c, -s_], [0, s_, c]]
        elif axis == 'Y': r = [[c, 0, s_], [0, 1, 0], [-s_, 0, c]]
        else: r = [[c, -s_, 0], [s_, c, 0], [0, 0, 1]]
        m = np.eye(n); m[:3, :3] = r; return Matrix(m)
    def __matmul__(s, o):
        if isinstance(o, Matrix): return Matrix(s.m @ o.m)
        v = _arr(o)
        if s.m.shape[0] == 4 and len(v) == 3: return Vector((s.m @ np.append(v, 1))[:3])
        return Vector(s.m @ v)
